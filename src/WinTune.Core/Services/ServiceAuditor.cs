using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Startup;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Services;

/// <summary>Serviço auditado.</summary>
/// <param name="Info">Dados do serviço.</param>
/// <param name="Publisher">Fabricante (CompanyName do executável).</param>
/// <param name="IsMicrosoft">Serviço da Microsoft (somente leitura).</param>
/// <param name="CanSetManual">Está na allowlist e pode ir de Automático para Manual.</param>
/// <param name="CpuPercent">CPU do processo hospedeiro.</param>
/// <param name="MemoryMb">Memória privada do processo hospedeiro.</param>
/// <param name="Note">Observação.</param>
public sealed record AuditedService(ServiceInfo Info, string? Publisher, bool IsMicrosoft, bool CanSetManual, double? CpuPercent, double? MemoryMb, string Note);

/// <summary>Tarefa de atualizador de terceiros que pode ser desabilitada.</summary>
/// <param name="Task">Tarefa.</param>
/// <param name="TweakId">ID do ajuste.</param>
public sealed record AuditedTask(ScheduledTaskInfo Task, string TweakId);

/// <summary>
/// Auditoria conservadora de serviços e tarefas (Fase 11). Só serviços/tarefas de atualizadores de terceiros, com nomes
/// exatos em código, podem ser alterados; nada é parado à força.
/// </summary>
public sealed class ServiceAuditor
{
    /// <summary>Prefixo de ID de serviço.</summary>
    public const string ServicePrefix = "service-manual:";

    /// <summary>Prefixo de ID de tarefa.</summary>
    public const string TaskPrefix = "task-disable:";

    /// <summary>
    /// Serviços de atualizadores de terceiros que podem ir de Automático para Manual (nomes exatos; confirmar na máquina).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ServiceAllowlist { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AdobeARMservice"] = "Atualizador do Adobe Acrobat/Reader",
        ["gupdate"] = "Atualizador do Google (legado)",
        ["gupdatem"] = "Atualizador do Google (legado)",
        ["brave"] = "Atualizador do Brave",
        ["bravem"] = "Atualizador do Brave",
        ["MozillaMaintenance"] = "Serviço de manutenção da Mozilla",
        ["LGHUBUpdaterService"] = "Atualizador do Logitech G HUB",
    };

    /// <summary>Prefixos de caminho de tarefas de atualizadores de terceiros que podem ser desabilitadas.</summary>
    public static IReadOnlyList<string> TaskAllowlistPrefixes { get; } =
    [
        @"\GoogleUpdateTaskMachine",
        @"\Adobe Acrobat Update Task",
        @"\BraveSoftwareUpdateTaskMachine",
    ];

    private readonly IServiceControl _services;
    private readonly IScheduledTasks? _tasks;
    private readonly IFileSystem _fs;
    private readonly IEnvironmentInfo _env;
    private readonly IProcessUsageReader? _usage;

    /// <summary>Cria o auditor.</summary>
    public ServiceAuditor(IServiceControl services, IScheduledTasks? tasks, IFileSystem fs, IEnvironmentInfo env, IProcessUsageReader? usage)
    {
        _services = services;
        _tasks = tasks;
        _fs = fs;
        _env = env;
        _usage = usage;
    }

    /// <summary>Lista os serviços com classificação e consumo.</summary>
    public IReadOnlyList<AuditedService> ListServices()
    {
        var usage = new Dictionary<int, ProcessUsage>();
        try
        {
            foreach (var u in _usage?.Read() ?? [])
            {
                usage.TryAdd(u.ProcessId, u);
            }
        }
#pragma warning disable CA1031 // Consumo é opcional.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        var windows = Path.TrimEndingDirectorySeparator(_env.GetFolder(KnownFolder.Windows));
        return _services.GetServices()
            .Select(s =>
            {
                var exe = s.ImagePath is null ? null : StartupManager.ExtractExecutable(s.ImagePath);
                var publisher = exe is null ? null : _fs.GetFileCompany(exe);
                var signer = exe is null ? null : _fs.GetSignerSubject(exe);
                var isMs = Contains(publisher, "Microsoft") || Contains(signer, "Microsoft") ||
                           (exe?.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ?? false);
                var allowlisted = ServiceAllowlist.ContainsKey(s.Name) && !isMs;
                var can = allowlisted && s.Config.StartMode == ServiceStartMode.Automatic;
                usage.TryGetValue(s.ProcessId, out var u);
                var note = isMs ? "Serviço da Microsoft: somente leitura."
                    : allowlisted ? (can ? ServiceAllowlist[s.Name] + ": pode ir para Manual." : ServiceAllowlist[s.Name] + ": já não é automático.")
                    : "Não classificado: somente leitura.";
                return new AuditedService(s, publisher, isMs, can, s.ProcessId == 0 ? null : u?.CpuPercent, s.ProcessId == 0 ? null : u?.PrivateMemoryMb, note);
            })
            .OrderByDescending(a => a.CanSetManual)
            .ThenByDescending(a => a.MemoryMb ?? 0)
            .ToList();
    }

    /// <summary>Tarefas de atualizadores de terceiros (allowlist por prefixo, autor não-Microsoft).</summary>
    public IReadOnlyList<AuditedTask> ListUpdaterTasks()
    {
        if (_tasks is null)
        {
            return [];
        }

        return _tasks.GetTasks()
            .Where(IsAllowlistedTask)
            .Select(t => new AuditedTask(t, TaskId(t.Path)))
            .ToList();
    }

    /// <summary>Cria o ajuste "Automático → Manual" (null se não permitido).</summary>
    public Tweak? CreateServiceTweak(string serviceName)
    {
        var s = ListServices().FirstOrDefault(a => string.Equals(a.Info.Name, serviceName, StringComparison.OrdinalIgnoreCase));
        return s is not null && (s.CanSetManual || ServiceAllowlist.ContainsKey(s.Info.Name) && !s.IsMicrosoft)
            ? new ServiceManualTweak(_services, s.Info.Name)
            : null;
    }

    /// <summary>Cria o ajuste que desabilita a tarefa (null se não permitido).</summary>
    public Tweak? CreateTaskTweak(string taskPath)
    {
        var t = ListUpdaterTasks().FirstOrDefault(a => string.Equals(a.Task.Path, taskPath, StringComparison.OrdinalIgnoreCase));
        return t is null || _tasks is null ? null : new TaskDisableTweak(_tasks, t.Task.Path, t.TweakId);
    }

    /// <summary>Reconstrói ajustes a partir do ID (só nomes da allowlist em código / tarefas existentes que casam com a allowlist).</summary>
    public Tweak? Resolve(string tweakId)
    {
        if (tweakId.StartsWith(ServicePrefix, StringComparison.Ordinal))
        {
            var name = tweakId[ServicePrefix.Length..];
            var allowed = ServiceAllowlist.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            return allowed is null ? null : new ServiceManualTweak(_services, allowed);
        }

        if (tweakId.StartsWith(TaskPrefix, StringComparison.Ordinal) && _tasks is not null)
        {
            var t = _tasks.GetTasks().Where(IsAllowlistedTask).FirstOrDefault(x => TaskId(x.Path) == tweakId);
            return t is null ? null : new TaskDisableTweak(_tasks, t.Path, tweakId);
        }

        return null;
    }

    internal static string TaskId(string path) =>
        TaskPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..12].ToLowerInvariant();

    private static bool IsAllowlistedTask(ScheduledTaskInfo t) =>
        TaskAllowlistPrefixes.Any(p => t.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && !Contains(t.Author, "Microsoft");

    private static bool Contains(string? text, string value) => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>Muda um serviço da allowlist de Automático para Manual (não para o serviço). Reversível.</summary>
public sealed class ServiceManualTweak : Tweak
{
    private const string ModeKey = "startMode";
    private const string DelayedKey = "delayedAutoStart";
    private readonly IServiceControl _services;
    private readonly string _name;

    /// <summary>Cria o ajuste. O nome precisa estar em <see cref="ServiceAuditor.ServiceAllowlist"/>.</summary>
    public ServiceManualTweak(IServiceControl services, string serviceName)
    {
        if (!ServiceAuditor.ServiceAllowlist.ContainsKey(serviceName))
        {
            throw new ArgumentException("Serviço fora da allowlist.", nameof(serviceName));
        }

        _services = services;
        _name = serviceName;
    }

    /// <inheritdoc />
    public override string Id => ServiceAuditor.ServicePrefix + _name;

    /// <inheritdoc />
    public override string Name => $"Serviço {_name}: Automático → Manual";

    /// <inheritdoc />
    public override string Description =>
        $"{ServiceAuditor.ServiceAllowlist[_name]}. Passa a iniciar só quando necessário, em vez de a cada boot. O serviço não é parado agora.";

    /// <inheritdoc />
    public override Focus[] For => [Focus.Work, Focus.Gaming];

    /// <inheritdoc />
    public override Scenario Helps => Scenario.SlowBoot | Scenario.SlowMultitasking;

    /// <inheritdoc />
    public override string Mechanism => $"ChangeServiceConfig({_name}, SERVICE_DEMAND_START)";

    /// <inheritdoc />
    public override bool DefaultSelected => false;

    /// <inheritdoc />
    public override bool IsApplied() => _services.GetConfig(_name)?.StartMode == ServiceStartMode.Manual;

    /// <inheritdoc />
    public override IReadOnlyList<string> DescribeChanges() => [$"{_name}: {Describe(_services.GetConfig(_name))} → Manual"];

    /// <inheritdoc />
    public override TweakResult Apply(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var current = _services.GetConfig(_name);
        if (current is null)
        {
            return TweakResult.Skipped(Id, "Serviço não instalado.");
        }

        if (current.StartMode != ServiceStartMode.Automatic)
        {
            return TweakResult.Skipped(Id, $"O serviço já está em {Describe(current)}; nada a fazer.");
        }

        var op = store.BeginOperation(Id, "apply");
        var created = store.TryAddOriginal(new TweakBackup
        {
            TweakId = Id,
            Extras = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ModeKey] = current.StartMode.ToString(),
                [DelayedKey] = current.DelayedAutoStart.ToString(CultureInfo.InvariantCulture),
            },
        });
        try
        {
            _services.SetConfig(_name, new ServiceConfig(ServiceStartMode.Manual, false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (created)
            {
                store.Remove(Id);
            }

            store.CompleteOperation(op);
            store.Journal("apply", Id, "failed", ex.Message);
            return TweakResult.Failed(Id, ex.Message);
        }

        store.CompleteOperation(op);
        store.Journal("apply", Id, "success");
        return TweakResult.Success(Id, "Serviço em Manual (vale no próximo boot).");
    }

    /// <inheritdoc />
    public override bool CanRevert(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.HasBackup(Id);
    }

    /// <inheritdoc />
    public override TweakResult Revert(BackupStore store, RevertMode mode)
    {
        ArgumentNullException.ThrowIfNull(store);
        var backup = store.Get(Id);
        if (backup is null)
        {
            return TweakResult.Skipped(Id, "Nada para desfazer.");
        }

        // Só aceita o valor que o próprio WinTune grava como original (Automático, com ou sem atraso).
        if (!backup.Extras.TryGetValue(ModeKey, out var modeText) ||
            !Enum.TryParse<ServiceStartMode>(modeText, out var originalMode) || originalMode != ServiceStartMode.Automatic)
        {
            store.Journal("revert", Id, "invalid-entry", modeText);
            return TweakResult.Failed(Id, "Backup do serviço inválido.");
        }

        var delayed = backup.Extras.TryGetValue(DelayedKey, out var d) && bool.TryParse(d, out var dv) && dv;
        var original = new ServiceConfig(originalMode, delayed);
        var current = _services.GetConfig(_name);
        if (current is null)
        {
            store.Remove(Id);
            store.Journal("revert", Id, "skipped", "serviço removido");
            return TweakResult.Skipped(Id, "O serviço não existe mais.");
        }

        if (current == original)
        {
            store.Remove(Id);
            store.Journal("revert", Id, "success", "já estava no original");
            return TweakResult.Success(Id, "Já estava no estado original.");
        }

        if (current.StartMode != ServiceStartMode.Manual && mode == RevertMode.Safe)
        {
            var msg = $"O tipo de inicialização foi alterado depois do WinTune (agora: {Describe(current)}); nada foi alterado.";
            store.Journal("revert", Id, "conflict", msg);
            return TweakResult.Conflict(Id, msg);
        }

        var op = store.BeginOperation(Id, "revert");
        try
        {
            _services.SetConfig(_name, original);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            store.CompleteOperation(op);
            store.Journal("revert", Id, "failed", ex.Message);
            return TweakResult.Failed(Id, ex.Message);
        }

        store.Remove(Id);
        store.CompleteOperation(op);
        store.Journal("revert", Id, "success");
        return TweakResult.Success(Id, $"Restaurado para {Describe(original)}.");
    }

    private static string Describe(ServiceConfig? c) => c is null
        ? "(não instalado)"
        : c.StartMode switch
        {
            ServiceStartMode.Automatic => c.DelayedAutoStart ? "Automático (atraso)" : "Automático",
            ServiceStartMode.Manual => "Manual",
            ServiceStartMode.Disabled => "Desabilitado",
            _ => c.StartMode.ToString(),
        };
}

/// <summary>Desabilita uma tarefa agendada de atualizador de terceiros (allowlist). Reversível.</summary>
public sealed class TaskDisableTweak : Tweak
{
    private const string EnabledKey = "enabled";
    private readonly IScheduledTasks _tasks;
    private readonly string _path;
    private readonly string _id;

    /// <summary>Cria o ajuste.</summary>
    public TaskDisableTweak(IScheduledTasks tasks, string taskPath, string tweakId)
    {
        _tasks = tasks;
        _path = taskPath;
        _id = tweakId;
    }

    /// <inheritdoc />
    public override string Id => _id;

    /// <inheritdoc />
    public override string Name => $"Tarefa {_path}: desabilitar";

    /// <inheritdoc />
    public override string Description => "Tarefa agendada de atualizador de terceiros. Desabilitar não apaga a tarefa; reative aqui ou em \"Desfazer tudo\".";

    /// <inheritdoc />
    public override Focus[] For => [Focus.Work, Focus.Gaming];

    /// <inheritdoc />
    public override string Mechanism => $"Agendador de Tarefas: {_path}.Enabled = false";

    /// <inheritdoc />
    public override bool DefaultSelected => false;

    /// <inheritdoc />
    public override bool IsApplied() => Find() is { Enabled: false };

    /// <inheritdoc />
    public override IReadOnlyList<string> DescribeChanges() => [$"{_path}: {(Find()?.Enabled == true ? "habilitada" : "desabilitada")} → desabilitada"];

    /// <inheritdoc />
    public override TweakResult Apply(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var t = Find();
        if (t is null)
        {
            return TweakResult.Skipped(Id, "Tarefa não encontrada.");
        }

        if (!t.Enabled)
        {
            return TweakResult.Skipped(Id, "A tarefa já está desabilitada.");
        }

        var op = store.BeginOperation(Id, "apply");
        var created = store.TryAddOriginal(new TweakBackup
        {
            TweakId = Id,
            Extras = new Dictionary<string, string>(StringComparer.Ordinal) { [EnabledKey] = "true" },
        });
        try
        {
            _tasks.SetEnabled(_path, false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            if (created)
            {
                store.Remove(Id);
            }

            store.CompleteOperation(op);
            store.Journal("apply", Id, "failed", ex.Message);
            return TweakResult.Failed(Id, ex.Message);
        }

        store.CompleteOperation(op);
        store.Journal("apply", Id, "success", _path);
        return TweakResult.Success(Id, "Tarefa desabilitada.");
    }

    /// <inheritdoc />
    public override bool CanRevert(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.HasBackup(Id);
    }

    /// <inheritdoc />
    public override TweakResult Revert(BackupStore store, RevertMode mode)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!store.HasBackup(Id))
        {
            return TweakResult.Skipped(Id, "Nada para desfazer.");
        }

        var t = Find();
        if (t is null || t.Enabled)
        {
            store.Remove(Id);
            store.Journal("revert", Id, "success", "já habilitada ou removida");
            return TweakResult.Success(Id, "A tarefa já está habilitada (ou não existe mais).");
        }

        var op = store.BeginOperation(Id, "revert");
        try
        {
            _tasks.SetEnabled(_path, true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            store.CompleteOperation(op);
            store.Journal("revert", Id, "failed", ex.Message);
            return TweakResult.Failed(Id, ex.Message);
        }

        store.Remove(Id);
        store.CompleteOperation(op);
        store.Journal("revert", Id, "success", _path);
        return TweakResult.Success(Id, "Tarefa reabilitada.");
    }

    private ScheduledTaskInfo? Find() => _tasks.GetTasks().FirstOrDefault(t => string.Equals(t.Path, _path, StringComparison.OrdinalIgnoreCase));
}
