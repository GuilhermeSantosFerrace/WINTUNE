using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>Sensores simulados: devolve leituras de uma fila (repete a última).</summary>
public sealed class FakeSensors : IHardwareSensors
{
    private SensorReading _last = new();

    /// <summary>Leituras a devolver.</summary>
    public Queue<SensorReading> Readings { get; } = new();

    /// <summary>Fontes indisponíveis simuladas.</summary>
    public List<string> Unavailable { get; } = [];

    public IReadOnlyList<string> UnavailableSources => Unavailable;

    public int ReadCount { get; private set; }

    public SensorReading Read()
    {
        ReadCount++;
        if (Readings.Count > 0)
        {
            _last = Readings.Dequeue();
        }

        return _last;
    }

    public void Dispose()
    {
    }
}

/// <summary>Uso por processo simulado.</summary>
public sealed class FakeProcessUsageReader : IProcessUsageReader
{
    public List<ProcessUsage> Usage { get; } = [];

    public IReadOnlyList<ProcessUsage> Read() => Usage.ToList();
}
