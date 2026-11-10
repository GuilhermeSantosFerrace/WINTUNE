using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Benchmark;
using WinTune.Core.Domain;
using WinTune.Core.Telemetry;

namespace WinTune.Presentation.ViewModels;

/// <summary>Linha de rodada salva.</summary>
public sealed partial class BenchmarkRunItem : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public BenchmarkRunItem(BenchmarkRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public BenchmarkRun Run { get; }

    public string When => Run.StartedUtc.ToLocalTime().ToString("dd/MM HH:mm", PtBr);

    public string Game => Run.Game;

    public string Condition => Run.Condition;

    public string Fps => Run.Frames is null ? "sem frametime" : Run.Frames.AverageFps.ToString("0", PtBr) + " (1% low " + Run.Frames.OnePercentLowFps.ToString("0", PtBr) + ")";

    public string GpuTemp => Run.Telemetry.GpuMaxTemperatureC?.ToString("0", PtBr) ?? "—";

    public string Ram => Run.Telemetry.RamMaxUsedPercent?.ToString("0", PtBr) + "%";

    public string Duration => Run.Duration.ToString(@"mm\:ss", PtBr);
}

/// <summary>Aba Benchmark: rodadas antes/depois por jogo e comparação honesta (sem inventar FPS).</summary>
public sealed partial class BenchmarkViewModel : ObservableObject
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;
    private readonly BenchmarkStore _store;
    private TelemetrySession? _pending;

    public BenchmarkViewModel(AppServices services, IDialogService dialogs, Action<string> log, TelemetryViewModel telemetry)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        ArgumentNullException.ThrowIfNull(telemetry);
        _store = new BenchmarkStore(services.FileSystem, services.Environment);
        telemetry.SessionCompleted += (_, s) => SetPendingSession(s);
    }

    public ObservableCollection<BenchmarkRunItem> Runs { get; } = [];

    public ObservableCollection<string> Results { get; } = [];

    public IReadOnlyList<string> Games { get; } = ["siege", "cs2", "geral"];

    [ObservableProperty]
    private string _condition = "antes";

    [ObservableProperty]
    private string _selectedGame = "cs2";

    [ObservableProperty]
    private string? _csvPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRunCommand))]
    private bool _hasPendingSession;

    [ObservableProperty]
    private string _pendingText = "Grave uma sessão na aba Telemetria (mesmo mapa/cenário a cada rodada). Ela aparece aqui para ser salva como rodada.";

    [ObservableProperty]
    private string _verdict = string.Empty;

    [ObservableProperty]
    private double _thresholdPercent = 3;

    /// <summary>Recebe uma sessão de telemetria concluída.</summary>
    public void SetPendingSession(TelemetrySession session)
    {
        _pending = session ?? throw new ArgumentNullException(nameof(session));
        HasPendingSession = true;
        if (Games.Contains(session.Label))
        {
            SelectedGame = session.Label;
        }

        PendingText = $"Sessão pronta: \"{session.Label}\", {session.Samples.Count} amostras ({(session.EndedUtc - session.StartedUtc).TotalMinutes.ToString("0.0", PtBr)} min). " +
                      "Escolha a condição e, se tiver, o CSV do PresentMon desta mesma rodada.";
    }

    [RelayCommand]
    private void PickCsv()
    {
        CsvPath = _dialogs.PickOpenFile("CSV do PresentMon desta rodada", "CSV do PresentMon (*.csv)|*.csv");
    }

    private bool CanSave() => HasPendingSession;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveRunAsync()
    {
        if (_pending is null)
        {
            return;
        }

        var session = _pending;
        FrameStats? frames = null;
        if (!string.IsNullOrWhiteSpace(CsvPath))
        {
            try
            {
                var path = CsvPath;
                frames = await Task.Run(() =>
                {
                    var entry = _services.FileSystem.GetFile(path) ?? throw new FileNotFoundException(path);
                    if (entry.Length > PresentMonCsv.MaxBytes)
                    {
                        throw new InvalidDataException("CSV grande demais.");
                    }

                    var process = _services.Games.FirstOrDefault(g => string.Equals(g.Target.ToLabel(), SelectedGame, StringComparison.OrdinalIgnoreCase))?.ProcessNames.FirstOrDefault();
                    return FrameStats.From(PresentMonCsv.Parse(_services.FileSystem.ReadAllText(path), process), "PresentMon");
                }).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _dialogs.Inform("CSV do PresentMon", "Não foi possível ler o CSV: " + ex.Message + "\nA rodada será salva só com telemetria.");
            }
        }

        var run = new BenchmarkRun
        {
            Game = SelectedGame,
            Condition = Condition,
            StartedUtc = session.StartedUtc,
            Duration = session.EndedUtc - session.StartedUtc,
            Telemetry = TelemetrySummary.From(session.Samples),
            Frames = frames,
        };
        await Task.Run(() => _store.Save(run)).ConfigureAwait(true);
        _log($"Benchmark: rodada \"{run.Game}/{run.Condition}\" salva{(frames is null ? " (sem frametime)" : string.Empty)}.");
        _pending = null;
        HasPendingSession = false;
        CsvPath = null;
        PendingText = "Rodada salva. Grave a próxima na aba Telemetria.";
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var (runs, errors) = await Task.Run(() =>
        {
            var r = _store.LoadAll(out var e);
            return (r, e);
        }).ConfigureAwait(true);
        Runs.Clear();
        foreach (var r in runs)
        {
            Runs.Add(new BenchmarkRunItem(r));
        }

        foreach (var e in errors)
        {
            _log("Benchmark: arquivo ignorado — " + e);
        }
    }

    [RelayCommand]
    private void Compare()
    {
        var game = Runs.Select(r => r.Run).Where(r => string.Equals(r.Game, SelectedGame, StringComparison.OrdinalIgnoreCase)).ToList();
        var report = new Comparator { Threshold = Math.Clamp(ThresholdPercent, 0.5, 50) / 100.0 }
            .Compare(game.Where(r => r.Condition == "antes").ToList(), game.Where(r => r.Condition == "depois").ToList());

        Results.Clear();
        foreach (var m in report.Metrics)
        {
            Results.Add($"{m.Metric}: {m.Before.ToString("0.0", PtBr)} → {m.After.ToString("0.0", PtBr)} " +
                        $"({m.ChangePercent.ToString("+0.0;-0.0;0", PtBr)}%, ruído ±{m.Noise.ToString("0.0", PtBr)}) — {Text(m.Verdict)}");
        }

        foreach (var n in report.Notes)
        {
            Results.Add("Obs.: " + n);
        }

        Verdict = $"{report.Game}: {Text(report.Overall)}";
    }

    internal static string Text(Verdict v) => v switch
    {
        Core.Benchmark.Verdict.Improvement => "melhora",
        Core.Benchmark.Verdict.Regression => "piora",
        Core.Benchmark.Verdict.NoSignificantDifference => "sem diferença relevante",
        _ => "dados insuficientes",
    };
}
