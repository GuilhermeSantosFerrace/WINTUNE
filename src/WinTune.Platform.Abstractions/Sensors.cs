namespace WinTune.Platform.Abstractions;

/// <summary>Leitura instantânea dos sensores (null = métrica indisponível).</summary>
public sealed record SensorReading
{
    /// <summary>Uso total de CPU (%).</summary>
    public double? CpuTotalPercent { get; init; }

    /// <summary>Uso da thread lógica mais ocupada (%).</summary>
    public double? CpuMaxThreadPercent { get; init; }

    /// <summary>Clock efetivo médio da CPU (MHz).</summary>
    public double? CpuEffectiveClockMhz { get; init; }

    /// <summary>Temperatura aproximada (zona térmica ACPI), em °C.</summary>
    public double? CpuTemperatureC { get; init; }

    /// <summary>Uso da GPU (%).</summary>
    public double? GpuUtilizationPercent { get; init; }

    /// <summary>Temperatura da GPU (°C).</summary>
    public double? GpuTemperatureC { get; init; }

    /// <summary>Clock gráfico da GPU (MHz).</summary>
    public double? GpuClockMhz { get; init; }

    /// <summary>VRAM em uso (MB).</summary>
    public double? GpuMemoryUsedMb { get; init; }

    /// <summary>VRAM total (MB).</summary>
    public double? GpuMemoryTotalMb { get; init; }

    /// <summary>RAM em uso (%).</summary>
    public double? RamUsedPercent { get; init; }

    /// <summary>RAM disponível (MB).</summary>
    public double? RamAvailableMb { get; init; }

    /// <summary>Páginas por segundo (<c>Memory\Pages/sec</c>).</summary>
    public double? PagesPerSecond { get; init; }

    /// <summary>Ocupação do disco (%).</summary>
    public double? DiskBusyPercent { get; init; }
}

/// <summary>
/// Sensores de hardware. Implementações do WinTune usam somente fontes em modo usuário (PDH, WMI, NVML):
/// nenhum driver de kernel é carregado (risco R1, anti-cheat).
/// </summary>
public interface IHardwareSensors : IDisposable
{
    /// <summary>Lê os sensores (a primeira leitura de contadores de taxa pode vir vazia).</summary>
    SensorReading Read();

    /// <summary>Fontes indisponíveis, com o motivo (para a UI degradar com mensagem clara).</summary>
    IReadOnlyList<string> UnavailableSources { get; }
}

/// <summary>Uso de recursos por processo (para "consumidores em segundo plano").</summary>
/// <param name="Name">Nome do processo.</param>
/// <param name="ProcessId">PID.</param>
/// <param name="CpuPercent">CPU normalizada pelo total de threads (%).</param>
/// <param name="PrivateMemoryMb">Memória privada (MB).</param>
/// <param name="IoBytesPerSecond">E/S (bytes/s).</param>
public sealed record ProcessUsage(string Name, int ProcessId, double CpuPercent, double PrivateMemoryMb, double IoBytesPerSecond);

/// <summary>Leitura de uso por processo (somente leitura, sem abrir handles para os processos).</summary>
public interface IProcessUsageReader
{
    /// <summary>Uso atual por processo.</summary>
    IReadOnlyList<ProcessUsage> Read();
}
