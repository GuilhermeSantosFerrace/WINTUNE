using System.Security.Cryptography;
using System.Text;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Startup;

/// <summary>Origem do item de inicialização.</summary>
public enum StartupSource
{
    /// <summary>HKCU Run.</summary>
    RunUser,

    /// <summary>HKLM Run (64 bits).</summary>
    RunMachine,

    /// <summary>HKLM Run (32 bits, WOW6432Node).</summary>
    RunMachine32,

    /// <summary>HKCU RunOnce (somente leitura).</summary>
    RunOnceUser,

    /// <summary>HKLM RunOnce (somente leitura).</summary>
    RunOnceMachine,

    /// <summary>Pasta Inicializar do usuário.</summary>
    StartupFolderUser,

    /// <summary>Pasta Inicializar de todos os usuários.</summary>
    StartupFolderCommon,

    /// <summary>Tarefa agendada "Ao fazer logon" (somente leitura).</summary>
    ScheduledTaskLogon,
}

/// <summary>Estado no mecanismo nativo <c>StartupApproved</c> (o mesmo do Gerenciador de Tarefas).</summary>
public enum StartupState
{
    /// <summary>Habilitado (ou sem entrada em StartupApproved).</summary>
    Enabled,

    /// <summary>Desabilitado.</summary>
    Disabled,

    /// <summary>Valor desconhecido (não é alterado).</summary>
    Unknown,

    /// <summary>Não se aplica (RunOnce, tarefas).</summary>
    NotApplicable,
}

/// <summary>Item de inicialização.</summary>
/// <param name="Id">ID estável (usado como ID de ajuste no backup).</param>
/// <param name="Source">Origem.</param>
/// <param name="Name">Nome do valor/arquivo/tarefa.</param>
/// <param name="Command">Linha de comando.</param>
/// <param name="ExecutablePath">Executável deduzido.</param>
/// <param name="Publisher">Editor (CompanyName).</param>
/// <param name="Signer">Assinante declarado (Authenticode, sem validar cadeia).</param>
/// <param name="State">Estado.</param>
/// <param name="Impact">Impacto estimado (Alto/Médio/Baixo/Desconhecido).</param>
/// <param name="BlockedReason">Motivo do bloqueio (null = pode ser desativado).</param>
public sealed record StartupItem(
    string Id,
    StartupSource Source,
    string Name,
    string Command,
    string? ExecutablePath,
    string? Publisher,
    string? Signer,
    StartupState State,
    string Impact,
    string? BlockedReason)
{
    /// <summary>Pode ser desativado pelo WinTune.</summary>
    public bool CanDisable => BlockedReason is null && State == StartupState.Enabled;
}

/// <summary>
/// Gerenciador de inicialização: lista Run/RunOnce/pasta Inicializar/tarefas ao logon e desativa de forma reversível via
/// <c>StartupApproved</c>. Nunca remove itens; itens da Microsoft, de segurança e de drivers ficam bloqueados.
/// </summary>
public sealed class StartupManager
{
    /// <summary>Prefixo dos IDs de ajuste de inicialização.</summary>
    public const string IdPrefix = "startup-item:";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private static readonly string[] SecurityVendors =
        ["ESET", "Kaspersky", "Bitdefender", "Norton", "NortonLifeLock", "Gen Digital", "McAfee", "Avast", "AVG", "Malwarebytes", "Sophos", "Trend Micro", "Avira", "Webroot",
         "Cisco", "CrowdStrike", "SentinelOne", "Palo Alto Networks", "Zscaler", "Fortinet", "Check Point"];

    private static readonly string[] HardwareVendors =
        ["Realtek", "Intel", "Advanced Micro Devices", "NVIDIA", "Synaptics", "ELAN", "Conexant", "Dolby", "Waves"];

    private static readonly string[] HighImpactNames =
        ["Steam", "Discord", "EpicGamesLauncher", "EADesktop", "UbisoftConnect", "Ubisoft Game Launcher", "Spotify", "Battle.net",
         "Razer Synapse", "iCUE", "LGHUB", "ArmouryCrate", "Adobe Creative Cloud", "GoogleDriveFS", "Opera", "Brave", "Overwolf"];

    private static readonly string[] ProtectedNames = ["SecurityHealth", "WindowsDefender", "ctfmon"];

    private readonly IRegistry _registry;
    private readonly IFileSystem _fs;
    private readonly IEnvironmentInfo _env;
    private readonly IScheduledTasks? _tasks;

    /// <summary>Cria o gerenciador.</summary>
    public StartupManager(IRegistry registry, IFileSystem fs, IEnvironmentInfo env, IScheduledTasks? tasks)
    {
        _registry = registry;
        _fs = fs;
        _env = env;
        _tasks = tasks;
    }

    /// <summary>Lista todos os itens (somente leitura).</summary>
    public IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();
        AddRun(items, RegistryHive.CurrentUser, RunKey, StartupSource.RunUser);
        AddRun(items, RegistryHive.LocalMachine, RunKey, StartupSource.RunMachine);
        AddRun(items, RegistryHive.LocalMachine, Run32Key, StartupSource.RunMachine32);
        AddRun(items, RegistryHive.CurrentUser, RunOnceKey, StartupSource.RunOnceUser);
        AddRun(items, RegistryHive.LocalMachine, RunOnceKey, StartupSource.RunOnceMachine);
        AddFolder(items, _env.GetFolder(KnownFolder.Startup), StartupSource.StartupFolderUser);
        AddFolder(items, _env.GetFolder(KnownFolder.CommonStartup), StartupSource.StartupFolderCommon);
        AddTasks(items);
        return items;
    }

    /// <summary>Cria o ajuste reversível que desativa o item (null se bloqueado ou não suportado).</summary>
    public Tweak? CreateDisableTweak(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.BlockedReason is not null)
        {
            return null;
        }

        var approved = ApprovedLocation(item.Source);
        return approved is null ? null : new StartupDisableTweak(_registry, item, approved.Value.Hive, approved.Value.Key);
    }

    /// <summary>
    /// Reconstrói o ajuste a partir do ID, procurando apenas em locais fixos em código e nomes que existem no registro/pastas
    /// reais (nunca a partir de caminhos do arquivo de backup).
    /// </summary>
    public Tweak? Resolve(string tweakId)
    {
        if (!tweakId.StartsWith(IdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var item = List().FirstOrDefault(i => i.Id == tweakId);
        if (item is not null)
        {
            var approved = ApprovedLocation(item.Source);
            return approved is null ? null : new StartupDisableTweak(_registry, item, approved.Value.Hive, approved.Value.Key);
        }

        // O item pode ter sido removido da origem, mas a entrada StartupApproved ainda existe.
        foreach (var source in new[] { StartupSource.RunUser, StartupSource.RunMachine, StartupSource.RunMachine32, StartupSource.StartupFolderUser, StartupSource.StartupFolderCommon })
        {
            var loc = ApprovedLocation(source)!.Value;
            foreach (var name in _registry.GetValueNames(loc.Hive, loc.Key))
            {
                if (MakeId(source, name) == tweakId)
                {
                    var orphan = new StartupItem(tweakId, source, name, string.Empty, null, null, null, StartupState.Unknown, "Desconhecido", null);
                    return new StartupDisableTweak(_registry, orphan, loc.Hive, loc.Key);
                }
            }
        }

        return null;
    }

    /// <summary>Interpreta o binário de <c>StartupApproved</c> (V17: 02 = habilitado, 03 = desabilitado).</summary>
    public static StartupState ParseApproved(RegistryValue? value)
    {
        if (value is null)
        {
            return StartupState.Enabled;
        }

        var data = value.Binary;
        if (value.Kind != RegistryValueKind.Binary || data is null || data.Length == 0)
        {
            return StartupState.Unknown;
        }

        return data[0] switch
        {
            0x02 => StartupState.Enabled,
            0x03 => StartupState.Disabled,
            _ => StartupState.Unknown,
        };
    }

    /// <summary>Extrai o executável de uma linha de comando.</summary>
    public static string? ExtractExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }

        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : c.Split(' ')[0];
    }

    internal static string MakeId(StartupSource source, string name)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{name.ToUpperInvariant()}")))[..12].ToLowerInvariant();
        return $"{IdPrefix}{source.ToString().ToLowerInvariant()}-{hash}";
    }

    private static (RegistryHive Hive, string Key)? ApprovedLocation(StartupSource source) => source switch
    {
        StartupSource.RunUser => (RegistryHive.CurrentUser, ApprovedBase + @"\Run"),
        StartupSource.RunMachine => (RegistryHive.LocalMachine, ApprovedBase + @"\Run"),
        StartupSource.RunMachine32 => (RegistryHive.LocalMachine, ApprovedBase + @"\Run32"),
        StartupSource.StartupFolderUser => (RegistryHive.CurrentUser, ApprovedBase + @"\StartupFolder"),
        StartupSource.StartupFolderCommon => (RegistryHive.LocalMachine, ApprovedBase + @"\StartupFolder"),
        _ => null,
    };

    private void AddRun(List<StartupItem> items, RegistryHive hive, string key, StartupSource source)
    {
        foreach (var name in _registry.GetValueNames(hive, key))
        {
            if (name.Length == 0)
            {
                continue;
            }

            var command = _registry.GetString(hive, key, name) ?? string.Empty;
            items.Add(Build(source, name, command, ExtractExecutable(command)));
        }
    }

    private void AddFolder(List<StartupItem> items, string folder, StartupSource source)
    {
        if (!_fs.DirectoryExists(folder))
        {
            return;
        }

        foreach (var file in _fs.EnumerateFiles(folder, "*", recursive: false))
        {
            var name = Path.GetFileName(file.FullPath);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file.FullPath : null;
            items.Add(Build(source, name, file.FullPath, exe));
        }
    }

    private void AddTasks(List<StartupItem> items)
    {
        if (_tasks is null)
        {
            return;
        }

        IReadOnlyList<ScheduledTaskInfo> tasks;
        try
        {
            tasks = _tasks.GetTasks();
        }
#pragma warning disable CA1031 // O Agendador pode estar inacessível; a listagem continua sem tarefas.
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        foreach (var t in tasks.Where(t => t.HasLogonTrigger))
        {
            var exe = t.ActionPaths.Select(p => Environment.ExpandEnvironmentVariables(p.Trim('"'))).FirstOrDefault();
            var item = Build(StartupSource.ScheduledTaskLogon, t.Path, exe ?? string.Empty, exe);
            items.Add(item with
            {
                State = t.Enabled ? StartupState.Enabled : StartupState.Disabled,
                BlockedReason = item.BlockedReason ?? "Tarefas agendadas são somente leitura nesta tela.",
            });
        }
    }

    private StartupItem Build(StartupSource source, string name, string command, string? exe)
    {
        var publisher = exe is null ? null : _fs.GetFileCompany(exe);
        var signer = exe is null ? null : _fs.GetSignerSubject(exe);
        var approved = ApprovedLocation(source);
        var state = approved is null ? StartupState.NotApplicable : ParseApproved(_registry.GetValue(approved.Value.Hive, approved.Value.Key, name));
        var size = exe is null ? null : _fs.GetFile(exe)?.Length;
        var impact = HighImpactNames.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase) || (exe?.Contains(h, StringComparison.OrdinalIgnoreCase) ?? false))
            ? "Alto"
            : size switch
            {
                null => "Desconhecido",
                > 50L * 1024 * 1024 => "Alto",
                > 10L * 1024 * 1024 => "Médio",
                _ => "Baixo",
            };

        string? blocked = null;
        if (Contains(signer, "Microsoft") || Contains(publisher, "Microsoft"))
        {
            blocked = "Item da Microsoft: protegido.";
        }
        else if (ProtectedNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            blocked = "Componente de segurança do Windows: protegido.";
        }
        else if (SecurityVendors.Any(v => Contains(publisher, v) || Contains(signer, v)))
        {
            blocked = "Software de segurança: protegido.";
        }
        else if (HardwareVendors.Any(v => Contains(publisher, v) || Contains(signer, v)))
        {
            blocked = "Driver/componente de hardware: protegido.";
        }
        else if (source is StartupSource.RunOnceUser or StartupSource.RunOnceMachine)
        {
            blocked = "RunOnce roda uma única vez: somente leitura.";
        }
        else if (state == StartupState.Unknown)
        {
            blocked = "Estado desconhecido em StartupApproved: não alterado.";
        }

        return new StartupItem(MakeId(source, name), source, name, command, exe, publisher, signer, state, impact, blocked);
    }

    private static bool Contains(string? text, string value) => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Desativa um item via <c>StartupApproved</c> (binário <c>03 00 00 00</c> + FILETIME); reversível pelo backup.</summary>
public sealed class StartupDisableTweak : RegistryTweak
{
    /// <summary>Cria o ajuste.</summary>
    public StartupDisableTweak(IRegistry registry, StartupItem item, RegistryHive hive, string approvedKey)
        : base(registry, new TweakInfo
        {
            Id = (item ?? throw new ArgumentNullException(nameof(item))).Id,
            Name = $"Inicialização: {item.Name}",
            Description = "Desativa a abertura automática no login usando o mesmo mecanismo do Gerenciador de Tarefas. O item não é removido.",
            For = [Focus.Work, Focus.Gaming],
            Helps = Scenario.SlowBoot,
            Priority = 10,
            DefaultSelected = false,
        },
        [
            new RegistrySetting(hive, approvedKey, item.Name,
                desired: _ => RegistryValue.FromBinary(DisabledValue(DateTime.UtcNow)),
                isSatisfied: v => StartupManager.ParseApproved(v) == StartupState.Disabled),
        ])
    {
        Item = item;
    }

    /// <summary>Item de origem.</summary>
    public StartupItem Item { get; }

    /// <summary>Binário "desabilitado": <c>03 00 00 00</c> seguido do FILETIME (UTC) da desativação.</summary>
    public static byte[] DisabledValue(DateTime utc)
    {
        var data = new byte[12];
        data[0] = 0x03;
        BitConverter.GetBytes(utc.ToFileTimeUtc()).CopyTo(data, 4);
        return data;
    }
}
