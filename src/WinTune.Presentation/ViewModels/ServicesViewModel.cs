using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Domain;
using WinTune.Core.Services;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Presentation.ViewModels;

/// <summary>Linha de serviço.</summary>
public sealed partial class ServiceItemViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public ServiceItemViewModel(AuditedService s, bool changedByWinTune)
    {
        Service = s ?? throw new ArgumentNullException(nameof(s));
        ChangedByWinTune = changedByWinTune;
    }

    public AuditedService Service { get; }

    public bool ChangedByWinTune { get; }

    public string Name => Service.Info.Name;

    public string DisplayName => Service.Info.DisplayName;

    public string State => Service.Info.State;

    public string StartMode => Service.Info.Config.StartMode switch
    {
        ServiceStartMode.Automatic => Service.Info.Config.DelayedAutoStart ? "Automático (atraso)" : "Automático",
        ServiceStartMode.Manual => ChangedByWinTune ? "Manual (pelo WinTune)" : "Manual",
        ServiceStartMode.Disabled => "Desabilitado",
        _ => Service.Info.Config.StartMode.ToString(),
    };

    public string Publisher => Service.Publisher ?? "—";

    public string Cpu => Service.CpuPercent?.ToString("0.0", PtBr) + (Service.CpuPercent is null ? "—" : " %");

    public string Memory => Service.MemoryMb is null ? "—" : Service.MemoryMb.Value.ToString("0", PtBr) + " MB";

    public string Note => Service.Note;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Linha de tarefa de atualizador.</summary>
public sealed partial class TaskItemViewModel : ObservableObject
{
    public TaskItemViewModel(AuditedTask t, bool changedByWinTune)
    {
        Task = t ?? throw new ArgumentNullException(nameof(t));
        ChangedByWinTune = changedByWinTune;
    }

    public AuditedTask Task { get; }

    public bool ChangedByWinTune { get; }

    public string Path => Task.Task.Path;

    public string Author => Task.Task.Author ?? "—";

    public string State => Task.Task.Enabled ? "Habilitada" : ChangedByWinTune ? "Desabilitada (pelo WinTune)" : "Desabilitada";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Aba Serviços: auditoria conservadora; só atualizadores de terceiros da allowlist são alteráveis.</summary>
public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;

    public ServicesViewModel(AppServices services, IDialogService dialogs, Action<string> log)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public ObservableCollection<ServiceItemViewModel> Services { get; } = [];

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    [ObservableProperty]
    private string _status = "Clique em Atualizar para auditar serviços e tarefas.";

    private ServiceAuditor? Auditor => _services.ServiceAuditor;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Auditor is null)
        {
            Status = "Auditoria indisponível.";
            return;
        }

        var (services, tasks) = await Task.Run(() => (Auditor.ListServices(), Auditor.ListUpdaterTasks())).ConfigureAwait(true);
        var ours = _services.Backup.TweakIds.ToHashSet(StringComparer.Ordinal);
        Services.Clear();
        foreach (var s in services)
        {
            Services.Add(new ServiceItemViewModel(s, ours.Contains(ServiceAuditor.ServicePrefix + s.Info.Name)));
        }

        Tasks.Clear();
        foreach (var t in tasks)
        {
            Tasks.Add(new TaskItemViewModel(t, ours.Contains(t.TweakId)));
        }

        Status = $"{Services.Count} serviços ({Services.Count(s => s.Service.CanSetManual)} alteráveis); {Tasks.Count} tarefa(s) de atualizador.";
    }

    [RelayCommand]
    private async Task SetManualAsync()
    {
        var targets = Services.Where(s => s.IsSelected && s.Service.CanSetManual).ToList();
        if (targets.Count == 0 || Auditor is null)
        {
            _dialogs.Inform("Serviços", "Selecione serviços marcados como \"pode ir para Manual\".");
            return;
        }

        if (!_dialogs.Confirm("Serviços: Automático → Manual",
                "Os serviços abaixo passam a iniciar só quando necessário. Nenhum serviço é parado agora; vale no próximo boot.\n\n" +
                string.Join("\n", targets.Select(t => "• " + t.Name))))
        {
            return;
        }

        foreach (var t in targets)
        {
            await Run(Auditor.CreateServiceTweak(t.Name), apply: true, t.Name).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreServicesAsync()
    {
        foreach (var t in Services.Where(s => s.IsSelected && s.ChangedByWinTune).ToList())
        {
            await Run(Auditor?.Resolve(ServiceAuditor.ServicePrefix + t.Name), apply: false, t.Name).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DisableTasksAsync()
    {
        var targets = Tasks.Where(t => t.IsSelected && t.Task.Task.Enabled).ToList();
        if (targets.Count == 0 || Auditor is null)
        {
            return;
        }

        if (!_dialogs.Confirm("Desabilitar tarefas", "Desabilitar (sem apagar) as tarefas de atualizador:\n\n" + string.Join("\n", targets.Select(t => "• " + t.Path))))
        {
            return;
        }

        foreach (var t in targets)
        {
            await Run(Auditor.CreateTaskTweak(t.Path), apply: true, t.Path).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EnableTasksAsync()
    {
        foreach (var t in Tasks.Where(t => t.IsSelected && t.ChangedByWinTune).ToList())
        {
            await Run(Auditor?.Resolve(t.Task.TweakId), apply: false, t.Path).ConfigureAwait(true);
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private async Task Run(Tweak? tweak, bool apply, string label)
    {
        if (tweak is null)
        {
            return;
        }

        var result = await Task.Run(() => apply ? tweak.Apply(_services.Backup) : tweak.Revert(_services.Backup, RevertMode.Safe)).ConfigureAwait(true);
        if (!apply && result.Status == TweakStatus.Conflict && _dialogs.Confirm("Valor alterado por outro programa", result.Message + "\n\nRestaurar mesmo assim?"))
        {
            result = await Task.Run(() => tweak.Revert(_services.Backup, RevertMode.Force)).ConfigureAwait(true);
        }

        _log($"{label}: {MainViewModel.StatusText(result)}. {result.Message}");
    }
}
