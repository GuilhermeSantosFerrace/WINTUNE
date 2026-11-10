using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Domain;
using WinTune.Core.Profiles;

namespace WinTune.Presentation.ViewModels;

/// <summary>Opção de ajuste para o modo jogo de um jogo.</summary>
public sealed partial class GameModeTweakOption : ObservableObject
{
    public GameModeTweakOption(string game, string id, string name, bool isChecked)
    {
        Game = game;
        Id = id;
        Name = name;
        _isChecked = isChecked;
    }

    public string Game { get; }

    public string Id { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Aba Perfis e modo jogo automático (opt-in).</summary>
public sealed partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;
    private readonly MainViewModel _main;
    private readonly ProfileStore _store;
    private readonly GameModeOrchestrator _orchestrator;
    private GameModeSettings _settings;
    private CancellationTokenSource? _pollCts;
    private readonly bool _loading;

    public ProfilesViewModel(AppServices services, IDialogService dialogs, Action<string> log, MainViewModel main)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _main = main ?? throw new ArgumentNullException(nameof(main));
        _store = new ProfileStore(services.FileSystem, services.Environment);
        _orchestrator = new GameModeOrchestrator(services.Processes, services.Games, services.Catalog.Find, services.Backup, _store, services.Clock);
        _settings = _store.LoadSettings();

        _loading = true;
        foreach (var game in new[] { "siege", "cs2" })
        {
            var selected = _settings.PerGame.TryGetValue(game, out var ids) ? ids : [];
            foreach (var t in services.Catalog.All.Where(t => t.For.Contains(Focus.Gaming)))
            {
                var option = new GameModeTweakOption(game, t.Id, t.Name, selected.Contains(t.Id));
                option.PropertyChanged += (_, _) => PersistSettings();
                (game == "siege" ? SiegeTweaks : Cs2Tweaks).Add(option);
            }
        }

        _isGameModeEnabled = _settings.Enabled;
        _loading = false;
        RefreshProfiles();
    }

    /// <summary>Disparado quando o modo jogo é ligado/desligado (o App mostra/oculta o ícone na bandeja).</summary>
    public event EventHandler<bool>? GameModeEnabledChanged;

    /// <summary>Intervalo de verificação de processos.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public ObservableCollection<UserProfile> Profiles { get; } = [];

    /// <inheritdoc />
    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public ObservableCollection<GameModeTweakOption> SiegeTweaks { get; } = [];

    public ObservableCollection<GameModeTweakOption> Cs2Tweaks { get; } = [];

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private UserProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isGameModeEnabled;

    [ObservableProperty]
    private string _gameModeStatus = "Modo jogo automático desligado.";

    partial void OnIsGameModeEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        PersistSettings();
        GameModeEnabledChanged?.Invoke(this, value);
        if (value)
        {
            StartPolling();
        }
        else
        {
            _ = StopPollingAsync();
        }
    }

    /// <summary>Na abertura do app: detecta modo jogo pendente (queda) e oferece restaurar; retoma o monitoramento se ligado.</summary>
    public async Task InitializeAsync()
    {
        var pending = _store.LoadState();
        if (pending is not null &&
            _dialogs.Confirm("Modo jogo pendente",
                $"O WinTune foi fechado com o modo jogo ativo ({pending.Game}). Restaurar agora o estado anterior dos ajustes do modo jogo?"))
        {
            foreach (var e in await Task.Run(_orchestrator.RestorePending).ConfigureAwait(true))
            {
                _log(e.Message);
            }
        }

        if (IsGameModeEnabled)
        {
            GameModeEnabledChanged?.Invoke(this, true);
            StartPolling();
        }
    }

    /// <summary>Executa uma verificação (usado pelo loop e pelos testes).</summary>
    public async Task PollOnceAsync()
    {
        var settings = _settings;
        var events = await Task.Run(() => _orchestrator.Poll(settings)).ConfigureAwait(true);
        foreach (var e in events)
        {
            _log("Modo jogo: " + e.Message);
        }

        GameModeStatus = _orchestrator.ActiveGame is { } g
            ? $"Modo jogo ATIVO ({g}). Ao fechar o jogo, o estado anterior é restaurado."
            : IsGameModeEnabled ? "Aguardando um jogo (verifica a cada 5 s, só pelo nome do processo)." : "Modo jogo automático desligado.";
    }

    [RelayCommand]
    private void SaveCurrent()
    {
        var name = NewProfileName.Trim();
        if (!ProfileStore.IsValidName(name))
        {
            _dialogs.Inform("Perfis", "Nome inválido: use letras, números, espaço, - e _ (até 40 caracteres).");
            return;
        }

        _store.SaveProfile(_main.CaptureProfile(name));
        _log($"Perfil \"{name}\" salvo.");
        NewProfileName = string.Empty;
        RefreshProfiles();
    }

    [RelayCommand]
    private async Task LoadSelectedAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await _main.ApplyProfileAsync(SelectedProfile).ConfigureAwait(true);
        _log($"Perfil \"{SelectedProfile.Name}\" carregado. Revise e clique em Aplicar selecionados.");
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedProfile is null || !_dialogs.Confirm("Perfis", $"Apagar o perfil \"{SelectedProfile.Name}\"?"))
        {
            return;
        }

        _store.DeleteProfile(SelectedProfile.Name);
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var p in _store.ListProfiles())
        {
            Profiles.Add(p);
        }
    }

    private void PersistSettings()
    {
        if (_loading)
        {
            return;
        }

        _settings = new GameModeSettings
        {
            Enabled = IsGameModeEnabled,
            PerGame = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["siege"] = SiegeTweaks.Where(o => o.IsChecked).Select(o => o.Id).ToList(),
                ["cs2"] = Cs2Tweaks.Where(o => o.IsChecked).Select(o => o.Id).ToList(),
            },
        };
        _store.SaveSettings(_settings);
    }

    private void StartPolling()
    {
        if (_pollCts is not null)
        {
            return;
        }

        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Loop();

        async Task Loop()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await PollOnceAsync().ConfigureAwait(true);
                    await Task.Delay(PollInterval, token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                // Desligado.
            }
        }
    }

    private async Task StopPollingAsync()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        foreach (var e in await Task.Run(_orchestrator.RestorePending).ConfigureAwait(true))
        {
            _log("Modo jogo: " + e.Message);
        }

        GameModeStatus = "Modo jogo automático desligado.";
    }
}
