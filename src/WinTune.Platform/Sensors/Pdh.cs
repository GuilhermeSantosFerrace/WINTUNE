using System.Runtime.InteropServices;

namespace WinTune.Platform.Sensors;

/// <summary>
/// Consulta PDH com nomes de contador em inglês (<c>PdhAddEnglishCounter</c>), independente do idioma do Windows.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;

    private readonly IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.Ordinal);
    private bool _disposed;

    public PdhQuery()
    {
        var status = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (status != 0)
        {
            throw new InvalidOperationException($"PdhOpenQuery falhou (0x{status:X8}).");
        }
    }

    /// <summary>Adiciona um contador; retorna falso se ele não existir nesta máquina.</summary>
    public bool TryAdd(string path)
    {
        if (_counters.ContainsKey(path))
        {
            return true;
        }

        var status = PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter);
        if (status != 0)
        {
            return false;
        }

        _counters[path] = counter;
        return true;
    }

    public bool Has(string path) => _counters.ContainsKey(path);

    public bool Collect() => PdhCollectQueryData(_query) == 0;

    public double? Value(string path)
    {
        if (!_counters.TryGetValue(path, out var counter))
        {
            return null;
        }

        var status = PdhGetFormattedCounterValue(counter, PdhFmtDouble | PdhFmtNoCap100, out _, out var value);
        return status == 0 && value.CStatus is 0 or 1 ? value.DoubleValue : null;
    }

    /// <summary>Valores de um contador curinga (<c>*</c>), por instância.</summary>
    public IReadOnlyList<(string Instance, double Value)> Array(string path)
    {
        if (!_counters.TryGetValue(path, out var counter))
        {
            return [];
        }

        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out var count, IntPtr.Zero);
        if (status != PdhMoreData || size == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out count, buffer);
            if (status != 0)
            {
                return [];
            }

            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
            var result = new List<(string, double)>((int)count);
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + (i * itemSize));
                if (item.Value.CStatus is 0 or 1)
                {
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? string.Empty, item.Value.DoubleValue));
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _ = PdhCloseQuery(_query);
            _disposed = true;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)]
        public uint CStatus;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
