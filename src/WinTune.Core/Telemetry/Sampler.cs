using WinTune.Platform.Abstractions;

namespace WinTune.Core.Telemetry;

/// <summary>Amostra de telemetria com horário e rótulo (ex.: jogo detectado).</summary>
/// <param name="Timestamp">Horário (UTC).</param>
/// <param name="Reading">Leitura dos sensores.</param>
/// <param name="Label">Rótulo (<c>siege</c>, <c>cs2</c>, ...).</param>
public sealed record TelemetrySample(DateTimeOffset Timestamp, SensorReading Reading, string? Label = null);

/// <summary>Buffer circular de capacidade fixa.</summary>
/// <typeparam name="T">Tipo do item.</typeparam>
public sealed class CircularBuffer<T>
{
    private readonly T[] _items;
    private int _start;

    /// <summary>Cria o buffer.</summary>
    public CircularBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    /// <summary>Quantidade de itens.</summary>
    public int Count { get; private set; }

    /// <summary>Capacidade.</summary>
    public int Capacity => _items.Length;

    /// <summary>Adiciona, descartando o mais antigo quando cheio.</summary>
    public void Add(T item)
    {
        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = item;
            Count++;
        }
        else
        {
            _items[_start] = item;
            _start = (_start + 1) % _items.Length;
        }
    }

    /// <summary>Itens do mais antigo para o mais novo.</summary>
    public IReadOnlyList<T> ToList()
    {
        var list = new List<T>(Count);
        for (var i = 0; i < Count; i++)
        {
            list.Add(_items[(_start + i) % _items.Length]);
        }

        return list;
    }

    /// <summary>Os últimos <paramref name="n"/> itens.</summary>
    public IReadOnlyList<T> Last(int n) => ToList().TakeLast(n).ToList();
}

/// <summary>Sessão de telemetria gravada.</summary>
/// <param name="Label">Rótulo (jogo ou atividade).</param>
/// <param name="StartedUtc">Início.</param>
/// <param name="EndedUtc">Fim.</param>
/// <param name="Samples">Amostras.</param>
public sealed record TelemetrySession(string Label, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc, IReadOnlyList<TelemetrySample> Samples);

/// <summary>Amostragem (padrão 1 Hz) com buffer circular e modo sessão.</summary>
public sealed class Sampler
{
    private readonly IHardwareSensors _sensors;
    private readonly IClock _clock;
    private readonly CircularBuffer<TelemetrySample> _buffer;
    private readonly object _gate = new();
    private List<TelemetrySample>? _session;
    private string? _sessionLabel;
    private DateTimeOffset _sessionStart;

    /// <summary>Cria o amostrador.</summary>
    public Sampler(IHardwareSensors sensors, IClock clock, int capacity = 3600)
    {
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _buffer = new CircularBuffer<TelemetrySample>(capacity);
    }

    /// <summary>Fontes indisponíveis.</summary>
    public IReadOnlyList<string> UnavailableSources => _sensors.UnavailableSources;

    /// <summary>Indica se há sessão em gravação.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _session is not null;
            }
        }
    }

    /// <summary>Amostras recentes.</summary>
    public IReadOnlyList<TelemetrySample> Recent(int n)
    {
        lock (_gate)
        {
            return _buffer.Last(n);
        }
    }

    /// <summary>Lê os sensores uma vez e guarda a amostra.</summary>
    public TelemetrySample SampleOnce(string? label = null)
    {
        var sample = new TelemetrySample(_clock.UtcNow, _sensors.Read(), label ?? _sessionLabel);
        lock (_gate)
        {
            _buffer.Add(sample);
            _session?.Add(sample);
        }

        return sample;
    }

    /// <summary>Inicia uma sessão de gravação.</summary>
    public void StartSession(string label)
    {
        lock (_gate)
        {
            _session = [];
            _sessionLabel = label;
            _sessionStart = _clock.UtcNow;
        }
    }

    /// <summary>Encerra a sessão e devolve as amostras.</summary>
    public TelemetrySession StopSession()
    {
        lock (_gate)
        {
            var session = new TelemetrySession(_sessionLabel ?? "sessao", _sessionStart, _clock.UtcNow, _session?.ToList() ?? []);
            _session = null;
            _sessionLabel = null;
            return session;
        }
    }
}
