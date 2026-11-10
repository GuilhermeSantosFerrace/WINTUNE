using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinTune.Core.Backup;
using WinTune.Core.Benchmark;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Reports;

/// <summary>Módulo de memória (sem número de série).</summary>
/// <param name="CapacityGb">Capacidade.</param>
/// <param name="RatedMts">Velocidade nominal.</param>
/// <param name="ConfiguredMts">Velocidade configurada.</param>
public sealed record MemoryModule(double CapacityGb, ulong RatedMts, ulong ConfiguredMts);

/// <summary>Hardware no relatório (apenas modelos e configurações).</summary>
public sealed record HardwareSection
{
    /// <summary>CPU.</summary>
    public string Cpu { get; init; } = string.Empty;

    /// <summary>GPUs.</summary>
    public IReadOnlyList<string> Gpus { get; init; } = [];

    /// <summary>RAM total (GB).</summary>
    public double RamTotalGb { get; init; }

    /// <summary>Módulos.</summary>
    public IReadOnlyList<MemoryModule> MemoryModules { get; init; } = [];

    /// <summary>Sistema operacional.</summary>
    public string Os { get; init; } = string.Empty;

    /// <summary>Build.</summary>
    public int OsBuild { get; init; }
}

/// <summary>Ajuste aplicado no relatório.</summary>
/// <param name="Id">ID.</param>
/// <param name="Name">Nome.</param>
/// <param name="SinceUtc">Desde.</param>
public sealed record AppliedTweak(string Id, string Name, DateTimeOffset SinceUtc);

/// <summary>Relatório exportável.</summary>
public sealed record WinTuneReport
{
    /// <summary>Schema.</summary>
    public int Schema { get; init; } = 1;

    /// <summary>Versão do app.</summary>
    public required string AppVersion { get; init; }

    /// <summary>Gerado em.</summary>
    public DateTimeOffset GeneratedUtc { get; init; }

    /// <summary>Hardware.</summary>
    public required HardwareSection Hardware { get; init; }

    /// <summary>Achados.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Ajustes ativos.</summary>
    public IReadOnlyList<AppliedTweak> AppliedTweaks { get; init; } = [];

    /// <summary>Rodadas de benchmark.</summary>
    public IReadOnlyList<BenchmarkRun> BenchmarkRuns { get; init; } = [];

    /// <summary>Últimas operações.</summary>
    public IReadOnlyList<JournalEntry> RecentHistory { get; init; } = [];
}

/// <summary>Monta e exporta o relatório (arquivo local escolhido pelo usuário; nada sai da máquina).</summary>
public sealed class ReportBuilder
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly IWmiReader _wmi;
    private readonly IEnvironmentInfo _env;
    private readonly IClock _clock;

    /// <summary>Cria o construtor.</summary>
    public ReportBuilder(IWmiReader wmi, IEnvironmentInfo env, IClock clock)
    {
        _wmi = wmi;
        _env = env;
        _clock = clock;
    }

    /// <summary>Monta o relatório.</summary>
    public WinTuneReport Build(string appVersion, SystemProfile profile, IEnumerable<Finding> findings, BackupStore backup,
        Func<string, Tweak?> resolve, IEnumerable<BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(resolve);
        var modules = Try(() => _wmi.Query(WmiScopes.Cimv2, "SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory")
            .Select(r => new MemoryModule(Math.Round((r.GetUInt64("Capacity") ?? 0) / 1073741824.0, 1), r.GetUInt64("Speed") ?? 0, r.GetUInt64("ConfiguredClockSpeed") ?? 0))
            .ToList()) ?? [];
        var os = Try(() => _wmi.Query(WmiScopes.Cimv2, "SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault()?.GetString("Caption")) ?? "Windows";

        var report = new WinTuneReport
        {
            AppVersion = appVersion,
            GeneratedUtc = _clock.UtcNow,
            Hardware = new HardwareSection
            {
                Cpu = profile.CpuName,
                Gpus = profile.GpuNames,
                RamTotalGb = Math.Round(profile.TotalRamBytes / 1073741824.0, 1),
                MemoryModules = modules,
                Os = os.Trim(),
                OsBuild = _env.OsBuild,
            },
            Findings = findings?.ToList() ?? [],
            AppliedTweaks = backup.TweakIds.Select(id => new AppliedTweak(id, resolve(id)?.Name ?? id, backup.Get(id)?.CreatedUtc ?? default)).ToList(),
            BenchmarkRuns = runs?.ToList() ?? [],
            RecentHistory = backup.JournalEntries.TakeLast(100).ToList(),
        };
        return report;
    }

    /// <summary>JSON anonimizado.</summary>
    public string ToJson(WinTuneReport report) => Sanitize(JsonSerializer.Serialize(report, Json), json: true);

    /// <summary>Markdown anonimizado.</summary>
    public string ToMarkdown(WinTuneReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine("# Relatório do WinTune").AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Versão do app: {report.AppVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Gerado em: {report.GeneratedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr)}");
        sb.AppendLine("- Privacidade: sem nome de usuário, nome do computador, números de série ou chaves.").AppendLine();

        sb.AppendLine("## Hardware").AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- CPU: {report.Hardware.Cpu}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- GPU: {string.Join(", ", report.Hardware.Gpus)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- RAM: {report.Hardware.RamTotalGb.ToString("0.#", PtBr)} GB");
        foreach (var m in report.Hardware.MemoryModules)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  - módulo {m.CapacityGb.ToString("0.#", PtBr)} GB, nominal {m.RatedMts} MT/s, configurado {m.ConfiguredMts} MT/s");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"- Sistema: {report.Hardware.Os} (build {report.Hardware.OsBuild})").AppendLine();

        sb.AppendLine("## Diagnóstico").AppendLine();
        if (report.Findings.Count == 0)
        {
            sb.AppendLine("Nenhum achado (rode Analisar antes de exportar).");
        }

        foreach (var f in report.Findings)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **[{f.Severity}] {f.Title}** — {f.Message}{(f.Evidence is null ? string.Empty : $" _(evidência: {f.Evidence})_")}");
        }

        sb.AppendLine().AppendLine("## Ajustes ativos (aplicados pelo WinTune)").AppendLine();
        if (report.AppliedTweaks.Count == 0)
        {
            sb.AppendLine("Nenhum.");
        }

        foreach (var t in report.AppliedTweaks)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- `{t.Id}` — {t.Name} (desde {t.SinceUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", PtBr)})");
        }

        sb.AppendLine().AppendLine("## Benchmark").AppendLine();
        if (report.BenchmarkRuns.Count == 0)
        {
            sb.AppendLine("Nenhuma rodada salva.");
        }
        else
        {
            sb.AppendLine("| Quando | Jogo | Condição | FPS médio | 1% low | GPU máx. °C | RAM máx. % |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|");
            foreach (var r in report.BenchmarkRuns)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {r.StartedUtc.ToLocalTime().ToString("dd/MM HH:mm", PtBr)} | {r.Game} | {r.Condition} | {r.Frames?.AverageFps.ToString("0", PtBr) ?? "—"} | {r.Frames?.OnePercentLowFps.ToString("0", PtBr) ?? "—"} | {r.Telemetry.GpuMaxTemperatureC?.ToString("0", PtBr) ?? "—"} | {r.Telemetry.RamMaxUsedPercent?.ToString("0", PtBr) ?? "—"} |");
            }
        }

        sb.AppendLine().AppendLine("## Histórico recente").AppendLine();
        foreach (var j in report.RecentHistory.Reverse())
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {j.TimestampUtc.ToLocalTime().ToString("dd/MM HH:mm", PtBr)} · {j.Action} · {j.TweakId} · {j.Result}{(j.Message is null ? string.Empty : " · " + j.Message)}");
        }

        return Sanitize(sb.ToString(), json: false);
    }

    /// <summary>Remove nome de usuário, nome do computador e caminho do perfil.</summary>
    public string Sanitize(string text, bool json)
    {
        ArgumentNullException.ThrowIfNull(text);
        var profile = _env.GetFolder(KnownFolder.UserProfile);
        var result = text;
        foreach (var variant in new[] { profile, profile.Replace(@"\", @"\\", StringComparison.Ordinal) })
        {
            if (variant.Length > 3)
            {
                result = result.Replace(variant, json ? "%USERPROFILE%" : "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (_env.MachineName.Length >= 2)
        {
            result = result.Replace(_env.MachineName, "<computador>", StringComparison.OrdinalIgnoreCase);
        }

        if (_env.UserName.Length >= 2)
        {
            result = result.Replace(_env.UserName, "<usuário>", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static T? Try<T>(Func<T> f)
    {
        try
        {
            return f();
        }
#pragma warning disable CA1031 // Relatório é best-effort.
        catch (Exception)
#pragma warning restore CA1031
        {
            return default;
        }
    }
}
