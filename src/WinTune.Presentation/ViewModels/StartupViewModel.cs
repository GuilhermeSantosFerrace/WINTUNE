using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Domain;
using WinTune.Core.Startup;

namespace WinTune.Presentation.ViewModels;

/// <summary>Linha da lista de inicialização.</summary>
public sealed partial class StartupItemViewModel : ObservableObject
{
    public StartupItemViewModel(StartupItem item, bool disabledByWinTune)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        DisabledByWinTune = disabledByWinTune;
    }

    public StartupItem Item { get; }

    public bool DisabledByWinTune { get; }

    public string Name => Item.Name;

    public string Source => Item.Source switch
    {
        StartupSource.RunUser => "Registro (usuário)",
        StartupSource.RunMachine => "Registro (máquina)",
        StartupSource.RunMachine32 => "Registro (máquina, 32 bits)",
        StartupSource.RunOnceUser => "RunOnce (usuário)",
        StartupSource.RunOnceMachine => "RunOnce (máquina)",
        StartupSource.StartupFolderUser => "Pasta Inicializar (usuário)",
        StartupSource.StartupFolderCommon => "Pasta Inicializar (todos)",
        _ => "Tarefa ao fazer logon",
    };

    public string Publisher => Item.Publisher ?? Item.Signer ?? "—";

    public string Impact => Item.Impact;

    public string State => Item.State switch
    {
        StartupState.Enabled => "Habilitado",
        StartupState.Disabled => DisabledByWinTune ? "Desabilitado (pelo WinTune)" : "Desabilitado",
        StartupState.NotApplicable => "—",
        _ => "Desconhecido",
    };

    public string Notes => Item.BlockedReason ?? string.Empty;

    public string Path => Item.ExecutablePath ?? Item.Command;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Aba Inicialização: lista (padrão) e desativação reversível (experimental, desligada por padrão).</summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;

    public StartupViewModel(AppServices services, IDialogService dialogs, Action<string> log)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    /// <summary>
    /// Desativação experimental: o formato binário de StartupApproved só tem fontes de comunidade (V17). Desligado por padrão;
    /// ligue depois de conferir no Gerenciador de Tarefas que o resultado é o mesmo.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisableSelectedCommand))]
    private bool _allowDisable;

    [ObservableProperty]
    private string _status = "Clique em Atualizar para listar os itens de inicialização.";

    private StartupManager? Manager => _services.Startup;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Manager is null)
        {
            Status = "Gerenciador de inicialização indisponível.";
            return;
        }

        var items = await Task.Run(() => Manager.List()).ConfigureAwait(true);
        var ours = _services.Backup.TweakIds.ToHashSet(StringComparer.Ordinal);
        Items.Clear();
        foreach (var i in items.OrderBy(i => i.BlockedReason is not null).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Items.Add(new StartupItemViewModel(i, ours.Contains(i.Id)));
        }

        Status = $"{Items.Count} itens. {Items.Count(i => i.Item.CanDisable)} podem ser desativados; os demais estão protegidos ou são somente leitura.";
    }

    private bool CanDisable() => AllowDisable;

    [RelayCommand(CanExecute = nameof(CanDisable))]
    private async Task DisableSelectedAsync()
    {
        var targets = Items.Where(i => i.IsSelected && i.Item.CanDisable).ToList();
        if (targets.Count == 0 || Manager is null)
        {
            _dialogs.Inform("Inicialização", "Selecione itens habilitados que não estejam protegidos.");
            return;
        }

        if (!_dialogs.Confirm("Desativar na inicialização",
                "Os itens abaixo deixam de abrir no login (não são removidos; reative aqui ou em \"Desfazer tudo\"):\n\n" +
                string.Join("\n", targets.Select(t => "• " + t.Name))))
        {
            return;
        }

        foreach (var t in targets)
        {
            var tweak = Manager.CreateDisableTweak(t.Item);
            if (tweak is null)
            {
                continue;
            }

            var result = await Task.Run(() => tweak.Apply(_services.Backup)).ConfigureAwait(true);
            _log($"Inicialização — {t.Name}: {MainViewModel.StatusText(result)}. {result.Message}");
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReenableSelectedAsync()
    {
        var targets = Items.Where(i => i.IsSelected && i.DisabledByWinTune).ToList();
        if (targets.Count == 0 || Manager is null)
        {
            _dialogs.Inform("Inicialização", "Selecione itens desativados pelo WinTune para reativar.");
            return;
        }

        foreach (var t in targets)
        {
            var tweak = Manager.Resolve(t.Item.Id);
            if (tweak is null)
            {
                continue;
            }

            var result = await Task.Run(() => tweak.Revert(_services.Backup, RevertMode.Safe)).ConfigureAwait(true);
            if (result.Status == TweakStatus.Conflict && _dialogs.Confirm("Valor alterado por outro programa", result.Message + "\n\nReativar mesmo assim?"))
            {
                result = await Task.Run(() => tweak.Revert(_services.Backup, RevertMode.Force)).ConfigureAwait(true);
            }

            _log($"Inicialização — {t.Name}: {MainViewModel.StatusText(result)}. {result.Message}");
        }

        await RefreshAsync().ConfigureAwait(true);
    }
}
