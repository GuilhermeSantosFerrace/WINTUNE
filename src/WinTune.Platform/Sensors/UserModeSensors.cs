using WinTune.Platform.Abstractions;

namespace WinTune.Platform.Sensors;

/// <summary>
/// Sensores somente em modo usuário: PDH (CPU, RAM, paginação, disco, GPU genérica), NVML (GPU NVIDIA) e WMI
/// (zona térmica ACPI). Seguro para usar com jogos com anti-cheat abertos: nenhum driver de kernel é carregado.
/// </summary>
public sealed class UserModeSensors : IHardwareSensors
{
    private const string CpuUtility = @"\Processor Information(_Total)\% Processor Utility";
    private const string CpuTime = @"\Processor(_Total)\% Processor Time";
    private const string CpuThreads = @"\Processor Information(*)\% Processor Utility";
    private const string CpuPerformance = @"\Processor Information(_Total)\% Processor Performance";
    private const string AvailableMb = @"\Memory\Available MBytes";
    private const string PagesSec = @"\Memory\Pages/sec";
    private const string DiskIdle = @"\PhysicalDisk(_Total)\% Idle Time";
    private const string Gpu3d = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private const string GpuEncode = @"\GPU Engine(*engtype_VideoEncode)\Utilization Percentage";

    private readonly PdhQuery? _pdh;
    private readonly Nvml? _nvml;
    private readonly IWmiReader _wmi;
    private readonly double? _totalRamMb;
    private readonly double? _baseClockMhz;
    private readonly List<string> _unavailable = [];
    private int _reads;
    private double? _lastCpuTemp;

    /// <summary>Abre as fontes disponíveis e registra as ausentes.</summary>
    public UserModeSensors(IWmiReader wmi)
    {
        _wmi = wmi ?? throw new ArgumentNullException(nameof(wmi));
        try
        {
            _pdh = new PdhQuery();
            if (!_pdh.TryAdd(CpuUtility))
            {
                _pdh.TryAdd(CpuTime);
            }

            Require(_pdh.TryAdd(CpuThreads), "CPU por thread: contador indisponível.");
            Require(_pdh.TryAdd(CpuPerformance), "Clock efetivo da CPU: contador indisponível.");
            Require(_pdh.TryAdd(AvailableMb), "RAM disponível: contador indisponível.");
            Require(_pdh.TryAdd(PagesSec), "Paginação: contador indisponível.");
            Require(_pdh.TryAdd(DiskIdle), "Disco: contador indisponível.");
            _pdh.TryAdd(Gpu3d);
            _pdh.Collect();
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            _pdh = null;
            _unavailable.Add("Contadores de desempenho (PDH) indisponíveis: " + ex.Message);
        }

        _nvml = Nvml.TryOpen(out var nvmlReason);
        if (nvmlReason is not null)
        {
            _unavailable.Add(nvmlReason + " Uso da GPU vem do contador genérico do Windows; temperatura/clock/VRAM indisponíveis.");
        }

        _totalRamMb = TryWmi(() => _wmi.Query(WmiScopes.Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")
            .FirstOrDefault()?.GetUInt64("TotalPhysicalMemory") / 1048576.0);
        _baseClockMhz = TryWmi(() => (double?)_wmi.Query(WmiScopes.Cimv2, "SELECT MaxClockSpeed FROM Win32_Processor")
            .FirstOrDefault()?.GetUInt64("MaxClockSpeed"));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> UnavailableSources => _unavailable;

    /// <inheritdoc />
    public SensorReading Read()
    {
        _reads++;
        _pdh?.Collect();

        var cpuTotal = Clamp(_pdh?.Value(CpuUtility) ?? _pdh?.Value(CpuTime));
        var threads = _pdh?.Array(CpuThreads).Where(t => !t.Instance.Contains("_Total", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        var perf = _pdh?.Value(CpuPerformance);
        var avail = _pdh?.Value(AvailableMb);
        var idle = _pdh?.Value(DiskIdle);
        double? gpuGeneric = null;
        if (_pdh?.Has(Gpu3d) == true)
        {
            var engines = _pdh.Array(Gpu3d);
            gpuGeneric = engines.Count == 0 ? null : Clamp(engines.Sum(e => e.Value));
        }

        double? encoderGeneric = null;
        if (_pdh?.Has(GpuEncode) == true)
        {
            var enc = _pdh.Array(GpuEncode);
            encoderGeneric = enc.Count == 0 ? null : Clamp(enc.Sum(e => e.Value));
        }

        // Zona térmica ACPI via WMI: leitura a cada 5 amostras para manter o custo baixo.
        if (_reads % 5 == 1)
        {
            _lastCpuTemp = TryWmi(() =>
            {
                var tenthsKelvin = _wmi.Query(WmiScopes.Wmi, "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")
                    .Select(r => r.GetUInt64("CurrentTemperature") ?? 0).Where(v => v > 0).DefaultIfEmpty(0UL).Max();
                return tenthsKelvin == 0 ? (double?)null : Math.Round((tenthsKelvin / 10.0) - 273.15, 1);
            });
        }

        var memory = _nvml?.Memory;
        return new SensorReading
        {
            CpuTotalPercent = cpuTotal,
            CpuMaxThreadPercent = threads.Count == 0 ? null : Clamp(threads.Max(t => t.Value)),
            CpuEffectiveClockMhz = perf is null || _baseClockMhz is null ? null : Math.Round(perf.Value * _baseClockMhz.Value / 100.0),
            CpuTemperatureC = _lastCpuTemp,
            GpuUtilizationPercent = _nvml?.Utilization ?? gpuGeneric,
            GpuEncoderPercent = _nvml?.EncoderUtilization ?? encoderGeneric,
            GpuTemperatureC = _nvml?.TemperatureC,
            GpuClockMhz = _nvml?.GraphicsClockMhz,
            GpuMemoryUsedMb = memory?.UsedMb,
            GpuMemoryTotalMb = memory?.TotalMb,
            RamAvailableMb = avail,
            RamUsedPercent = avail is null || _totalRamMb is null or 0 ? null : Clamp(100.0 * (1 - (avail.Value / _totalRamMb.Value))),
            PagesPerSecond = _pdh?.Value(PagesSec),
            DiskBusyPercent = idle is null ? null : Clamp(100 - idle.Value),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pdh?.Dispose();
        _nvml?.Dispose();
    }

    private void Require(bool ok, string message)
    {
        if (!ok)
        {
            _unavailable.Add(message);
        }
    }

    private static double? Clamp(double? v) => v is null ? null : Math.Clamp(v.Value, 0, 100);

    private static T? TryWmi<T>(Func<T?> f)
    {
        try
        {
            return f();
        }
#pragma warning disable CA1031 // Fonte opcional: ausência vira "indisponível".
        catch (Exception)
#pragma warning restore CA1031
        {
            return default;
        }
    }
}

/// <summary>Uso por processo via contadores formatados do WMI (sem abrir handles para os processos).</summary>
public sealed class ProcessUsageReader : IProcessUsageReader
{
    private readonly IWmiReader _wmi;

    /// <summary>Cria o leitor.</summary>
    public ProcessUsageReader(IWmiReader wmi)
    {
        _wmi = wmi;
    }

    /// <inheritdoc />
    public IReadOnlyList<ProcessUsage> Read()
    {
        var logical = Environment.ProcessorCount;
        return _wmi.Query(WmiScopes.Cimv2,
                "SELECT Name, IDProcess, PercentProcessorTime, WorkingSetPrivate, IODataBytesPersec FROM Win32_PerfFormattedData_PerfProc_Process")
            .Select(r => new ProcessUsage(
                r.GetString("Name") ?? string.Empty,
                (int)(r.GetInt64("IDProcess") ?? 0),
                (r.GetUInt64("PercentProcessorTime") ?? 0) / (double)logical,
                (r.GetUInt64("WorkingSetPrivate") ?? 0) / 1048576.0,
                r.GetUInt64("IODataBytesPersec") ?? 0))
            .Where(p => p.Name is not ("_Total" or "Idle") && p.ProcessId != 0)
            .ToList();
    }
}
