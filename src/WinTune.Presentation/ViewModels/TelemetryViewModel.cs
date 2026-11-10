using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Domain;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;

namespace WinTune.Presentation.ViewModels;

/// <summary>Linha de consumidor em segundo plano.</summary>
public sealed class ConsumerItem
{
    public ConsumerItem(BackgroundConsumer c)
    {
        ArgumentNullException.ThrowIfNull(c);
        Name = c.Usage.Name;
        Cpu = c.Usage.CpuPercent.ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " %";
        Memory = c.Usage.PrivateMemoryMb.ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " MB";
        Category = c.KnownCategory ?? string.Empty;
    }

    public string Name { get; }

    public string Cpu { get; }

    public string Memory { get; }

    public string Category { get; }
}

/// <summary>Aba Telemetria: monitor ao vivo (1 Hz), sessão de diagnóstico e consumidores em segundo plano.</summary>
public sealed partial class TelemetryViewModel : ObservableObject
{
    private const int SeriesLength = 120;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly AppServices _services;
    private readonly BottleneckClassifier _classifier = new();
    private Sampler? _sampler;
    private IHardwareSensors? _sensors;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private DateTimeOffset _sessionEnds;

    public TelemetryViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>Disparado quando uma sessão de diagnóstico termina (usado pelo Benchmark).</summary>
    public event EventHandler<TelemetrySession>? SessionCompleted;

    /// <summary>Intervalo entre amostras (1 s; menor nos testes).</summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    public ObservableCollection<string> Evidence { get; } = [];

    public ObservableCollection<FindingItemViewModel> SessionFindings { get; } = [];

    public ObservableCollection<ConsumerItem> Consumers { get; } = [];

    public TelemetrySession? LastSession { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartMonitoringCommand), nameof(StopMonitoringCommand))]
    private bool _isMonitoring;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand), nameof(StopSessionCommand))]
    private bool _isSessionRecording;

    [ObservableProperty]
    private int _sessionMinutes = 5;

    [ObservableProperty]
    private string _sessionStatus = "Nenhuma sessão gravada.";

    [ObservableProperty]
    private string _sessionSummary = string.Empty;

    [ObservableProperty]
    private string _cpuText = "CPU: —";

    [ObservableProperty]
    private string _gpuText = "GPU: —";

    [ObservableProperty]
    private string _ramText = "RAM: —";

    [ObservableProperty]
    private string _diskText = "Disco: —";

    [ObservableProperty]
    private string _unavailableText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _cpuSeries = [];

    [ObservableProperty]
    private IReadOnlyList<double> _gpuSeries = [];

    [ObservableProperty]
    private IReadOnlyList<double> _ramSeries = [];

    public bool HasUnavailable => UnavailableText.Length > 0;

    partial void OnUnavailableTextChanged(string value) => OnPropertyChanged(nameof(HasUnavailable));

    private bool CanStart() => !IsMonitoring;

    private bool CanStop() => IsMonitoring;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartMonitoringAsync()
    {
        if (_services.SensorsFactory is null)
        {
            UnavailableText = "Sensores indisponíveis nesta configuração.";
            return;
        }

        IsMonitoring = true;
        try
        {
            _sensors ??= await Task.Run(_services.SensorsFactory).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Sensor indisponível degrada com mensagem.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UnavailableText = "Não foi possível iniciar os sensores: " + ex.Message;
            IsMonitoring = false;
            return;
        }

        _sampler ??= new Sampler(_sensors, _services.Clock);
        UnavailableText = string.Join(Environment.NewLine, _sampler.UnavailableSources);
        _monitorCts = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(_monitorCts.Token);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopMonitoringAsync()
    {
        if (IsSessionRecording)
        {
            FinishSession();
        }

        _monitorCts?.Cancel();
        if (_monitorTask is not null)
        {
            await _monitorTask.ConfigureAwait(true);
        }

        _monitorCts?.Dispose();
        _monitorCts = null;
        IsMonitoring = false;
    }

    private bool CanStartSession() => !IsSessionRecording;

    private bool CanStopSession() => IsSessionRecording;

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync()
    {
        if (!IsMonitoring)
        {
            await StartMonitoringAsync().ConfigureAwait(true);
            if (!IsMonitoring)
            {
                return;
            }
        }

        var label = DetectGameLabel();
        _sampler!.StartSession(label);
        _sessionEnds = _services.Clock.UtcNow + TimeSpan.FromMinutes(Math.Clamp(SessionMinutes, 1, 120));
        IsSessionRecording = true;
        SessionStatus = $"Gravando sessão \"{label}\"… use o PC/jogo normalmente.";
    }

    [RelayCommand(CanExecute = nameof(CanStopSession))]
    private void StopSession() => FinishSession();

    [RelayCommand]
    private async Task RefreshConsumersAsync()
    {
        if (_services.ProcessUsage is null)
        {
            return;
        }

        var inspector = new BackgroundLoadInspector(_services.ProcessUsage, _services.Games, _services.KnownBackgroundProcesses);
        var list = await Task.Run(() => inspector.Inspect()).ConfigureAwait(true);
        Consumers.Clear();
        foreach (var c in list)
        {
            Consumers.Add(new ConsumerItem(c));
        }
    }

    /// <summary>Lê uma amostra imediatamente (usado pelo loop e pelos testes).</summary>
    public async Task<TelemetrySample?> SampleNowAsync()
    {
        if (_sampler is null)
        {
            return null;
        }

        var sample = await Task.Run(() => _sampler.SampleOnce()).ConfigureAwait(true);
        Render(sample);
        if (IsSessionRecording && _services.Clock.UtcNow >= _sessionEnds)
        {
            FinishSession();
        }

        return sample;
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await SampleNowAsync().ConfigureAwait(true);
                await Task.Delay(SampleInterval, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Parado pelo usuário.
        }
    }

    private void FinishSession()
    {
        if (_sampler is null || !IsSessionRecording)
        {
            return;
        }

        var session = _sampler.StopSession();
        IsSessionRecording = false;
        LastSession = session;
        var report = _classifier.Classify(session.Samples);
        SessionSummary = report.Summary;
        Evidence.Clear();
        foreach (var e in report.Evidence)
        {
            Evidence.Add(e);
        }

        SessionFindings.Clear();
        foreach (var f in report.Findings)
        {
            SessionFindings.Add(new FindingItemViewModel(f));
        }

        SessionStatus = $"Sessão \"{session.Label}\" concluída: {session.Samples.Count} amostras.";
        SessionCompleted?.Invoke(this, session);
    }

    private string DetectGameLabel()
    {
        try
        {
            var names = _services.Processes.GetProcessNames();
            var running = _services.Games.FirstOrDefault(g => names.Any(g.MatchesProcess));
            return running?.Target.ToLabel() ?? "geral";
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return "geral";
        }
    }

    private void Render(TelemetrySample s)
    {
        var r = s.Reading;
        CpuText = $"CPU: {P(r.CpuTotalPercent)} (thread mais ocupada {P(r.CpuMaxThreadPercent)}) · {Mhz(r.CpuEffectiveClockMhz)}" +
                  (r.CpuTemperatureC is null ? string.Empty : $" · zona térmica {r.CpuTemperatureC.Value.ToString("0", PtBr)} °C");
        GpuText = $"GPU: {P(r.GpuUtilizationPercent)} · {Mhz(r.GpuClockMhz)}" +
                  (r.GpuTemperatureC is null ? string.Empty : $" · {r.GpuTemperatureC.Value.ToString("0", PtBr)} °C") +
                  (r.GpuMemoryUsedMb is null ? string.Empty : $" · VRAM {r.GpuMemoryUsedMb.Value.ToString("0", PtBr)}/{r.GpuMemoryTotalMb?.ToString("0", PtBr)} MB");
        RamText = $"RAM: {P(r.RamUsedPercent)} em uso · {(r.RamAvailableMb is null ? "—" : r.RamAvailableMb.Value.ToString("0", PtBr) + " MB livres")} · paginação {(r.PagesPerSecond is null ? "—" : r.PagesPerSecond.Value.ToString("0", PtBr) + "/s")}";
        DiskText = $"Disco: {P(r.DiskBusyPercent)} ocupado";
        CpuSeries = Push(CpuSeries, r.CpuTotalPercent);
        GpuSeries = Push(GpuSeries, r.GpuUtilizationPercent);
        RamSeries = Push(RamSeries, r.RamUsedPercent);
    }

    private static IReadOnlyList<double> Push(IReadOnlyList<double> series, double? value) =>
        series.Append(value ?? 0).TakeLast(SeriesLength).ToArray();

    private static string P(double? v) => v is null ? "—" : v.Value.ToString("0", PtBr) + "%";

    private static string Mhz(double? v) => v is null ? "clock —" : v.Value.ToString("0", PtBr) + " MHz";
}
