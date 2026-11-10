using System.Runtime.InteropServices;

namespace WinTune.Platform.Sensors;

/// <summary>
/// Leitura da GPU NVIDIA via NVML (<c>nvml.dll</c> instalada pelo driver em System32). Biblioteca em modo usuário;
/// não instala nem carrega driver próprio.
/// </summary>
internal sealed class Nvml : IDisposable
{
    private readonly IntPtr _lib;
    private readonly IntPtr _device;
    private readonly GetUtilization? _getUtilization;
    private readonly GetTemperature? _getTemperature;
    private readonly GetClock? _getClock;
    private readonly GetMemory? _getMemory;
    private readonly Shutdown? _shutdown;

    private Nvml(IntPtr lib, IntPtr device, GetUtilization? u, GetTemperature? t, GetClock? c, GetMemory? m, Shutdown? s)
    {
        _lib = lib;
        _device = device;
        _getUtilization = u;
        _getTemperature = t;
        _getClock = c;
        _getMemory = m;
        _shutdown = s;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Init();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Shutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHandleByIndex(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetUtilization(IntPtr device, out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetTemperature(IntPtr device, int sensor, out uint temperature);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetClock(IntPtr device, int clockType, out uint mhz);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetMemory(IntPtr device, out NvmlMemory memory);

    /// <summary>Tenta carregar a NVML; retorna null com o motivo se indisponível.</summary>
    public static Nvml? TryOpen(out string? reason)
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
        if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var lib))
        {
            reason = "GPU NVIDIA: nvml.dll não encontrada (sem driver NVIDIA).";
            return null;
        }

        var init = Get<Init>(lib, "nvmlInit_v2");
        var byIndex = Get<GetHandleByIndex>(lib, "nvmlDeviceGetHandleByIndex_v2");
        if (init is null || byIndex is null || init() != 0 || byIndex(0, out var device) != 0)
        {
            NativeLibrary.Free(lib);
            reason = "GPU NVIDIA: NVML não inicializou.";
            return null;
        }

        reason = null;
        return new Nvml(lib, device,
            Get<GetUtilization>(lib, "nvmlDeviceGetUtilizationRates"),
            Get<GetTemperature>(lib, "nvmlDeviceGetTemperature"),
            Get<GetClock>(lib, "nvmlDeviceGetClockInfo"),
            Get<GetMemory>(lib, "nvmlDeviceGetMemoryInfo"),
            Get<Shutdown>(lib, "nvmlShutdown"));
    }

    public double? Utilization => _getUtilization is not null && _getUtilization(_device, out var u) == 0 ? u.Gpu : null;

    public double? TemperatureC => _getTemperature is not null && _getTemperature(_device, 0, out var t) == 0 ? t : null;

    public double? GraphicsClockMhz => _getClock is not null && _getClock(_device, 0, out var c) == 0 ? c : null;

    public (double UsedMb, double TotalMb)? Memory =>
        _getMemory is not null && _getMemory(_device, out var m) == 0 ? (m.Used / 1048576.0, m.Total / 1048576.0) : null;

    public void Dispose()
    {
        _shutdown?.Invoke();
        NativeLibrary.Free(_lib);
    }

    private static T? Get<T>(IntPtr lib, string name)
        where T : Delegate =>
        NativeLibrary.TryGetExport(lib, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
