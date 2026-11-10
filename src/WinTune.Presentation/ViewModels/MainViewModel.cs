using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WinTune.Core.Backup;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Core.Games;
using WinTune.Core.Recommendations;
using WinTune.Core.Tweaks;

namespace WinTune.Presentation.ViewModels;

/// <summary>
/// Tela principal: foco, jogos, cenários, recomendações, diagnóstico e ações (Analisar, Aplicar, Desfazer tudo).
/// Toda operação longa roda fora da thread de UI (<see cref="Task.Run(Action)"/>) e aceita cancelamento.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly RecommendationEngine _engine = new();
    private int _refreshVersion;

    public MainViewModel(AppServices services, IDialogService dialogs, ILogger<MainViewModel>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _logger = (ILogger?)logger ?? NullLogger.Instance;

        Scenarios =
        [
            new(Scenario.Stutter, "Travadas (stutter)"),
            new(Scenario.LowFps, "FPS baixo"),
            new(Scenario.SlowMultitasking, "Lentidão com muitos apps"),
            new(Scenario.SlowBoot, "Boot lento"),
            new(Scenario.HighTemperature, "Temperatura alta"),
            new(Scenario.LongLoading, "Carregamentos longos"),
        ];
        foreach (var s in Scenarios)
        {
            s.PropertyChanged += (_, _) => _ = RefreshRecommendationsAsync();
        }

        if (services.Backup.LoadWarning is { } warning)
        {
            AddLog(warning);
        }

        Telemetry = new TelemetryViewModel(services);
    }

    /// <summary>Aba Telemetria.</summary>
    public TelemetryViewModel Telemetry { get; }

    public ObservableCollection<ScenarioOption> Scenarios { get; }

    public ObservableCollection<TweakItemViewModel> Recommendations { get; } = [];

    public ObservableCollection<FindingItemViewModel> SystemFindings { get; } = [];

    public ObservableCollection<FindingItemViewModel> GameFindings { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Perfil de hardware (após Analisar).</summary>
    public SystemProfile Profile { get; private set; } = SystemProfile.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGamesSectionVisible), nameof(IsWorkFocus), nameof(IsGamingFocus), nameof(IsBothFocus))]
    private Focus _selectedFocus = Focus.Gaming;

    [ObservableProperty]
    private bool _siegeSelected;

    [ObservableProperty]
    private bool _cs2Selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(ApplySelectedCommand), nameof(UndoAllCommand))]
    private AppState _state = AppState.Idle;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Pronto. Escolha o foco e clique em Analisar.";

    [ObservableProperty]
    private bool _hasAnalyzed;

    public bool IsBusy => State != AppState.Idle;

    public bool IsIdle => State == AppState.Idle;

    /// <summary>A seleção de jogos só aparece quando o foco inclui Jogos.</summary>
    public bool IsGamesSectionVisible => SelectedFocus.IncludesGaming();

    public bool IsWorkFocus
    {
        get => SelectedFocus == Focus.Work;
        set
        {
            if (value)
            {
                SelectedFocus = Focus.Work;
            }
        }
    }

    public bool IsGamingFocus
    {
        get => SelectedFocus == Focus.Gaming;
        set
        {
            if (value)
            {
                SelectedFocus = Focus.Gaming;
            }
        }
    }

    public bool IsBothFocus
    {
        get => SelectedFocus == Focus.Both;
        set
        {
            if (value)
            {
                SelectedFocus = Focus.Both;
            }
        }
    }

    /// <summary>Cenários marcados.</summary>
    public Scenario SelectedScenarios => Scenarios.Where(s => s.IsChecked).Aggregate(Scenario.None, (acc, s) => acc | s.Scenario);

    /// <summary>Jogos marcados (None se o foco não inclui Jogos).</summary>
    public GameTarget SelectedGames =>
        !SelectedFocus.IncludesGaming()
            ? GameTarget.None
            : (SiegeSelected ? GameTarget.RainbowSixSiege : GameTarget.None) | (Cs2Selected ? GameTarget.Cs2 : GameTarget.None);

    partial void OnSelectedFocusChanged(Focus value) => _ = RefreshRecommendationsAsync();

    partial void OnSiegeSelectedChanged(bool value) => _ = RefreshRecommendationsAsync();

    partial void OnCs2SelectedChanged(bool value) => _ = RefreshRecommendationsAsync();

    private bool CanRun() => State == AppState.Idle;

    /// <summary>Recalcula a lista de recomendações (lê o estado real fora da thread de UI).</summary>
    public async Task RefreshRecommendationsAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var request = new RecommendationRequest(SelectedFocus, SelectedScenarios, SelectedGames, Profile);
        var previousSelection = Recommendations.ToDictionary(r => r.Id, r => r.IsSelected, StringComparer.Ordinal);
        var recs = await Task.Run(() => _engine.Recommend(_services.Catalog.All, request)).ConfigureAwait(true);
        if (version != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        Recommendations.Clear();
        foreach (var r in recs)
        {
            var item = new TweakItemViewModel(r);
            if (!r.AlreadyApplied && previousSelection.TryGetValue(r.Tweak.Id, out var was))
            {
                item.IsSelected = was;
            }

            Recommendations.Add(item);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        State = AppState.Analyzing;
        StatusMessage = "Analisando o sistema…";
        Progress = 0;
        try
        {
            var games = SelectedGames;
            var (profile, system, game) = await Task.Run(() =>
            {
                var p = SystemProfileDetector.Detect(_services.Wmi, _services.Runner, _services.Environment);
                cancellationToken.ThrowIfCancellationRequested();
                var ctx = new DiagnosticContext(_services.Wmi, _services.Registry, _services.FileSystem, _services.Environment, _services.Clock, p);
                var sys = new Analyzer(Analyzer.StaticRules()).Run(ctx, cancellationToken);
                var gm = SelectedFocus.IncludesGaming()
                    ? new Analyzer(GameChecks.Rules(_services.Games, games)).Run(ctx, cancellationToken)
                    : [];
                return (p, sys, gm);
            }, cancellationToken).ConfigureAwait(true);

            Profile = profile;
            Replace(SystemFindings, system.Select(f => new FindingItemViewModel(f)));
            Replace(GameFindings, game.Select(f => new FindingItemViewModel(f)));
            Progress = 50;
            await RefreshRecommendationsAsync().ConfigureAwait(true);
            HasAnalyzed = true;
            Progress = 100;
            StatusMessage = $"Análise concluída: {Recommendations.Count} ajuste(s) recomendado(s), {SystemFindings.Count + GameFindings.Count} aviso(s).";
            AddLog(StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Análise cancelada.";
            AddLog(StatusMessage);
        }
        finally
        {
            State = AppState.Idle;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task ApplySelectedAsync(CancellationToken cancellationToken)
    {
        var selected = Recommendations.Where(r => r.IsSelected && !r.AlreadyApplied).ToList();
        if (selected.Count == 0)
        {
            _dialogs.Inform("Aplicar ajustes", "Nenhum ajuste selecionado (ou todos já estão aplicados).");
            return;
        }

        var summary = await Task.Run(() => BuildSummary(selected), cancellationToken).ConfigureAwait(true);
        if (!_dialogs.Confirm("Confirmar alterações", summary))
        {
            AddLog("Aplicação cancelada pelo usuário.");
            return;
        }

        State = AppState.Applying;
        Progress = 0;
        var reboot = false;
        try
        {
            StatusMessage = "Criando ponto de restauração…";
            var rp = await Task.Run(() => new RestorePointService(_services.Runner, _services.Backup).TryCreate(cancellationToken), cancellationToken)
                .ConfigureAwait(true);
            AddLog($"Ponto de restauração: {rp.Message}");

            for (var i = 0; i < selected.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = selected[i];
                StatusMessage = $"Aplicando: {item.Name}…";
                var result = await Task.Run(() => SafeApply(item.Tweak), cancellationToken).ConfigureAwait(true);
                item.Status = StatusText(result);
                reboot |= result is { Status: TweakStatus.Success, RequiresReboot: true };
                AddLog($"{item.Name}: {StatusText(result)}. {result.Message}");
                _logger.LogInformation("Apply {TweakId}: {Status} {Message}", result.TweakId, result.Status, result.Message);
                Progress = (i + 1) * 100.0 / selected.Count;
            }

            StatusMessage = "Ajustes aplicados.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Aplicação interrompida; os ajustes já concluídos continuam reversíveis.";
            AddLog(StatusMessage);
        }
        finally
        {
            State = AppState.Idle;
        }

        await RefreshRecommendationsAsync().ConfigureAwait(true);
        if (reboot)
        {
            _dialogs.Inform("Reinício necessário", "Um ou mais ajustes só passam a valer depois de reiniciar o Windows.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task UndoAllAsync()
    {
        var ids = _services.Backup.TweakIds;
        if (ids.Count == 0)
        {
            _dialogs.Inform("Desfazer tudo", "Não há ajustes do WinTune para desfazer.");
            return;
        }

        if (!_dialogs.Confirm("Desfazer tudo", $"Restaurar o estado original de {ids.Count} ajuste(s)?\n\n" +
                                                "Observação: a limpeza de arquivos não é reversível e não entra aqui."))
        {
            return;
        }

        State = AppState.Applying;
        Progress = 0;
        try
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var tweak = _services.Resolve(id);
                if (tweak is null)
                {
                    AddLog($"{id}: ajuste desconhecido nesta versão; ignorado (veja docs/SAFETY.md para recuperação manual).");
                    continue;
                }

                StatusMessage = $"Desfazendo: {tweak.Name}…";
                var result = await Task.Run(() => SafeRevert(tweak, RevertMode.Safe)).ConfigureAwait(true);
                if (result.Status == TweakStatus.Conflict &&
                    _dialogs.Confirm("Valor alterado por outro programa",
                        $"{tweak.Name}\n\n{result.Message}\n\nRestaurar o valor original mesmo assim?"))
                {
                    result = await Task.Run(() => SafeRevert(tweak, RevertMode.Force)).ConfigureAwait(true);
                }

                AddLog($"{tweak.Name}: {StatusText(result)}. {result.Message}");
                _logger.LogInformation("Revert {TweakId}: {Status} {Message}", result.TweakId, result.Status, result.Message);
                Progress = (i + 1) * 100.0 / ids.Count;
            }

            StatusMessage = "Desfazer concluído.";
        }
        finally
        {
            State = AppState.Idle;
        }

        await RefreshRecommendationsAsync().ConfigureAwait(true);
    }

    /// <summary>Cancela a operação em andamento (análise ou aplicação).</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (AnalyzeCancelCommand.CanExecute(null))
        {
            AnalyzeCancelCommand.Execute(null);
        }

        if (ApplySelectedCancelCommand.CanExecute(null))
        {
            ApplySelectedCancelCommand.Execute(null);
        }
    }

    internal static string StatusText(TweakResult r) => r.Status switch
    {
        TweakStatus.Success => "Sucesso",
        TweakStatus.Failed => "Falhou",
        TweakStatus.Skipped => "Ignorado",
        TweakStatus.Conflict => "Conflito",
        _ => r.Status.ToString(),
    };

    private string BuildSummary(IReadOnlyList<TweakItemViewModel> selected)
    {
        var sb = new StringBuilder("O WinTune vai alterar o seguinte (tudo reversível em \"Desfazer tudo\"):\n\n");
        foreach (var item in selected)
        {
            sb.Append("• ").Append(item.Name).Append(item.RequiresReboot ? " (exige reinício)" : string.Empty).AppendLine();
            try
            {
                foreach (var line in item.Tweak.DescribeChanges())
                {
                    sb.Append("    ").AppendLine(line);
                }
            }
#pragma warning disable CA1031 // A prévia é informativa; erro de leitura não impede a confirmação.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                sb.Append("    (não foi possível ler o estado atual: ").Append(ex.Message).AppendLine(")");
            }
        }

        sb.AppendLine().Append("Antes, será criado um ponto de restauração (se a Proteção do Sistema estiver ligada).");
        return sb.ToString();
    }

    private TweakResult SafeApply(Tweak tweak)
    {
        try
        {
            return tweak.Apply(_services.Backup);
        }
#pragma warning disable CA1031 // Um ajuste com erro inesperado não pode derrubar os demais.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Erro inesperado ao aplicar {TweakId}", tweak.Id);
            return TweakResult.Failed(tweak.Id, ex.Message);
        }
    }

    private TweakResult SafeRevert(Tweak tweak, RevertMode mode)
    {
        try
        {
            return tweak.Revert(_services.Backup, mode);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Erro inesperado ao reverter {TweakId}", tweak.Id);
            return TweakResult.Failed(tweak.Id, ex.Message);
        }
    }

    public void AddLog(string message) => Log.Insert(0, $"{_services.Clock.UtcNow.ToLocalTime():HH:mm:ss}  {message}");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items)
        {
            target.Add(i);
        }
    }
}
