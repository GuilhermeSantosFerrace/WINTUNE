using System.Globalization;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Diagnostics;

/// <summary>Consultas WMI compartilhadas pelas regras.</summary>
internal static class Wmi
{
    public const string PhysicalMemory = "SELECT Capacity, Speed, ConfiguredClockSpeed, DeviceLocator FROM Win32_PhysicalMemory";
    public const string Processor = "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor";
    public const string VideoController = "SELECT Name, DriverVersion, DriverDate, CurrentRefreshRate, MaxRefreshRate FROM Win32_VideoController";

    public static IReadOnlyList<WmiRow> Memory(DiagnosticContext c) => c.Wmi.Query(WmiScopes.Cimv2, PhysicalMemory);

    public static WmiRow? Cpu(DiagnosticContext c) => c.Wmi.Query(WmiScopes.Cimv2, Processor).FirstOrDefault();

    public static string Gb(ulong bytes) => (bytes / 1024d / 1024d / 1024d).ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " GB";
}

/// <summary><c>ram-below-rated</c>: memória rodando abaixo da velocidade nominal (EXPO/XMP desligado).</summary>
public sealed class RamBelowRatedRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "ram-below-rated";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var slow = Wmi.Memory(context)
            .Select(r => (Rated: r.GetUInt64("Speed") ?? 0, Configured: r.GetUInt64("ConfiguredClockSpeed") ?? 0))
            .Where(m => m.Rated > 0 && m.Configured > 0 && m.Configured < m.Rated)
            .ToList();
        if (slow.Count == 0)
        {
            yield break;
        }

        var m = slow[0];
        yield return new Finding(Id, "Memória abaixo da velocidade nominal",
            "A RAM está rodando abaixo da velocidade informada pelo módulo. Ative o perfil EXPO/XMP na BIOS/UEFI. " +
            "Em Ryzen 5000 o impacto costuma ser grande, porque a velocidade da memória define o FCLK. O WinTune não altera a BIOS.",
            Severity.Critical, $"Configurada: {m.Configured} MT/s; nominal: {m.Rated} MT/s");
    }
}

/// <summary><c>ram-single-channel</c>: apenas um pente detectado.</summary>
public sealed class RamSingleChannelRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "ram-single-channel";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var modules = Wmi.Memory(context);
        if (modules.Count == 1)
        {
            yield return new Finding(Id, "Memória em canal único",
                "Só um pente de memória foi detectado, então a RAM opera em canal único (metade da largura de banda). " +
                "Use dois pentes iguais nos slots indicados no manual da placa-mãe (dual channel).",
                Severity.Warning, $"Módulos: 1 ({modules[0].GetString("DeviceLocator") ?? "?"})");
        }
    }
}

/// <summary><c>ram-capacity-low</c>: 16 GB ou menos.</summary>
public sealed class RamCapacityLowRule : IDiagnosticRule
{
    /// <summary>Limite (16 GiB).</summary>
    public const ulong ThresholdBytes = 16UL * 1024 * 1024 * 1024;

    /// <inheritdoc />
    public string Id => "ram-capacity-low";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var total = Wmi.Memory(context).Aggregate(0UL, (sum, r) => sum + (r.GetUInt64("Capacity") ?? 0));
        if (total > 0 && total <= ThresholdBytes)
        {
            yield return new Finding(Id, "Pouca memória RAM para jogos modernos + navegador",
                "Com 16 GB ou menos, jogos atuais junto com navegador, Discord e launchers podem fazer o Windows usar o arquivo " +
                "de paginação, o que causa travadas. Considere 32 GB. A telemetria (sessão de diagnóstico) confirma se há paginação.",
                Severity.Warning, $"Total: {Wmi.Gb(total)}");
        }
    }
}

/// <summary><c>rebar-unknown</c>: Resizable BAR não é detectável por API.</summary>
public sealed class RebarUnknownRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "rebar-unknown";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        yield return new Finding(Id, "Resizable BAR: verificar manualmente",
            "O WinTune não consegue ler o estado do Resizable BAR. Confira no Painel de Controle NVIDIA (Informações do sistema) " +
            "ou no GPU-Z. Para ativar, ligue \"Above 4G Decoding\" e \"Resizable BAR\" na BIOS/UEFI.",
            Severity.Info, context.Profile.HasNvidiaGpu ? "GPU NVIDIA detectada" : null);
    }
}

/// <summary><c>vbs-active</c>: Segurança baseada em virtualização ativa (o app não desativa).</summary>
public sealed class VbsActiveRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "vbs-active";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var row = context.Wmi.Query(WmiScopes.DeviceGuard, "SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard").FirstOrDefault();
        if (row?.GetUInt64("VirtualizationBasedSecurityStatus") == 2)
        {
            yield return new Finding(Id, "Segurança baseada em virtualização (VBS) ativa",
                "A VBS/Integridade de Memória pode custar alguns % de FPS em certos jogos. É um recurso de segurança: a decisão é sua " +
                "e o WinTune não o desativa. Configuração em Segurança do Windows > Segurança do dispositivo > Isolamento de núcleo.",
                Severity.Info, "VirtualizationBasedSecurityStatus = 2 (em execução)");
        }
    }
}

/// <summary><c>chipset-driver-missing</c>: CPU AMD sem driver de chipset da AMD.</summary>
public sealed class ChipsetDriverMissingRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "chipset-driver-missing";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var vendor = context.Profile.CpuVendor.Length > 0 ? context.Profile.CpuVendor : Wmi.Cpu(context)?.GetString("Manufacturer") ?? string.Empty;
        if (!vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var amdSystemDrivers = context.Wmi
            .Query(WmiScopes.Cimv2, "SELECT DeviceName, DriverProviderName FROM Win32_PnPSignedDriver WHERE DeviceClass = 'SYSTEM'")
            .Count(r => IsAmdProvider(r.GetString("DriverProviderName")));
        if (amdSystemDrivers == 0)
        {
            yield return new Finding(Id, "Driver de chipset AMD não encontrado",
                "Nenhum driver de sistema com fornecedor AMD foi encontrado. Instale o driver de chipset oficial no site da AMD " +
                "(amd.com > Drivers e Suporte > Chipsets). O WinTune não baixa nada.",
                Severity.Warning, "Drivers de sistema AMD: 0");
        }
    }

    private static bool IsAmdProvider(string? provider) =>
        provider is not null &&
        (provider.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(provider.Trim(), "AMD", StringComparison.OrdinalIgnoreCase));
}

/// <summary><c>fast-startup</c>: Inicialização rápida ligada (informa, não muda).</summary>
public sealed class FastStartupRule : IDiagnosticRule
{
    /// <summary>Chave da inicialização rápida.</summary>
    public const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    /// <inheritdoc />
    public string Id => "fast-startup";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Registry.GetDWord(RegistryHive.LocalMachine, Key, "HiberbootEnabled") == 1)
        {
            yield return new Finding(Id, "Inicialização rápida ligada",
                "Com a Inicialização rápida, \"Desligar\" hiberna o kernel: drivers e alguns problemas persistem até um Reiniciar. " +
                "Se notar comportamento estranho após ligar o PC, use Reiniciar. O WinTune não altera esta opção.",
                Severity.Info, "HiberbootEnabled = 1");
        }
    }
}

/// <summary><c>disk-low-space</c>: disco do sistema com menos de 15% livre.</summary>
public sealed class DiskLowSpaceRule : IDiagnosticRule
{
    /// <summary>Limite de espaço livre.</summary>
    public const double Threshold = 0.15;

    /// <inheritdoc />
    public string Id => "disk-low-space";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var space = context.FileSystem.GetDriveSpace(context.Environment.SystemDrive);
        if (space is not null && space.TotalBytes > 0 && space.FreeFraction < Threshold)
        {
            yield return new Finding(Id, "Pouco espaço livre no disco do sistema",
                "Com menos de 15% livre, o SSD tem menos área para wear leveling e o cache de shaders/atualizações fica apertado. " +
                "Use a Limpeza segura do WinTune ou o Sensor de Armazenamento do Windows.",
                Severity.Warning, $"Livre: {space.FreeFraction:P0} de {Wmi.Gb((ulong)space.TotalBytes)}");
        }
    }
}

/// <summary><c>vcache-note</c>: CPU X3D de CCD único não precisa do "3D V-Cache Optimizer".</summary>
public sealed class VCacheNoteRule : IDiagnosticRule
{
    private static readonly string[] SingleCcdX3d = ["5600X3D", "5700X3D", "5800X3D", "7600X3D", "7800X3D", "9800X3D"];

    /// <inheritdoc />
    public string Id => "vcache-note";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Profile.CpuName.Length > 0 ? context.Profile.CpuName : Wmi.Cpu(context)?.GetString("Name") ?? string.Empty;
        var model = SingleCcdX3d.FirstOrDefault(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (model is not null)
        {
            yield return new Finding(Id, $"Ryzen {model}: CCD único com 3D V-Cache",
                "Esta CPU tem um único CCD, todo com 3D V-Cache. Ela não precisa do driver \"AMD 3D V-Cache Performance Optimizer\" " +
                "(feito para modelos com dois CCDs) nem de ajuste de afinidade de núcleos.",
                Severity.Info, name.Trim());
        }
    }
}

/// <summary><c>gpu-driver-old</c>: driver NVIDIA com mais de 180 dias.</summary>
public sealed class GpuDriverOldRule : IDiagnosticRule
{
    /// <summary>Idade considerada antiga.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);

    /// <summary>Cria a regra (o ID muda para a variante por jogo).</summary>
    public GpuDriverOldRule(string id = "gpu-driver-old", GameTarget games = GameTarget.None)
    {
        Id = id;
        Games = games;
    }

    /// <inheritdoc />
    public string Id { get; }

    private GameTarget Games { get; }

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var gpu in context.Wmi.Query(WmiScopes.Cimv2, Wmi.VideoController))
        {
            var name = gpu.GetString("Name") ?? string.Empty;
            if (!name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var date = ParseCimDate(gpu.GetString("DriverDate"));
            var version = NvidiaVersion(gpu.GetString("DriverVersion"));
            if (date is null)
            {
                continue;
            }

            var age = context.Clock.UtcNow - date.Value;
            if (age > MaxAge)
            {
                yield return new Finding(Id, "Driver da NVIDIA desatualizado",
                    "O driver de vídeo tem mais de 6 meses. Atualize pelo site oficial da NVIDIA ou pelo app NVIDIA. " +
                    "O WinTune não baixa nada.",
                    Severity.Info, $"{name}: driver {version ?? "?"} de {date.Value:dd/MM/yyyy} ({(int)age.TotalDays} dias)",
                    Games, Games == GameTarget.None ? FindingCategory.System : FindingCategory.Game);
            }
        }
    }

    /// <summary>Converte data CIM (<c>yyyyMMddHHmmss.ffffff+zzz</c>).</summary>
    public static DateTimeOffset? ParseCimDate(string? cim)
    {
        if (cim is null || cim.Length < 8)
        {
            return null;
        }

        return DateTime.TryParseExact(cim[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : null;
    }

    /// <summary>Converte a versão do Windows (ex.: <c>32.0.15.6094</c>) para a versão NVIDIA (<c>560.94</c>).</summary>
    public static string? NvidiaVersion(string? windowsVersion)
    {
        var parts = windowsVersion?.Split('.');
        if (parts is not { Length: 4 } || parts[2].Length < 1 || parts[3].Length != 4)
        {
            return null;
        }

        var digits = parts[2][^1..] + parts[3];
        return $"{digits[..3]}.{digits[3..]}";
    }
}
