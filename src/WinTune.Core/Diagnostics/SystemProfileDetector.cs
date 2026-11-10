using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Diagnostics;

/// <summary>Detecta o hardware para parametrizar recomendações. Tolerante a falhas parciais.</summary>
public static class SystemProfileDetector
{
    /// <summary>Detecta o perfil (somente leitura).</summary>
    public static SystemProfile Detect(IWmiReader wmi, IProcessRunner runner, IEnvironmentInfo environment)
    {
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(environment);

        var cpu = Try(() => wmi.Query(WmiScopes.Cimv2, Wmi.Processor).FirstOrDefault());
        var gpus = Try(() => wmi.Query(WmiScopes.Cimv2, "SELECT Name FROM Win32_VideoController")
                       .Select(r => r.GetString("Name") ?? string.Empty)
                       .Where(n => n.Length > 0 && !n.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) &&
                                   !n.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                       .ToList()) ?? [];
        var ram = Try(() => wmi.Query(WmiScopes.Cimv2, "SELECT Capacity FROM Win32_PhysicalMemory")
            .Aggregate(0UL, (s, r) => s + (r.GetUInt64("Capacity") ?? 0)));
        var ryzen = Try(() => PowerCfg.List(runner).Any(PowerCfg.IsAmdRyzenBalanced));

        return new SystemProfile
        {
            CpuName = cpu?.GetString("Name")?.Trim() ?? string.Empty,
            CpuVendor = cpu?.GetString("Manufacturer") ?? string.Empty,
            GpuNames = gpus,
            TotalRamBytes = ram,
            HasAmdRyzenBalancedPlan = ryzen,
            OsBuild = environment.OsBuild,
        };
    }

    private static T? Try<T>(Func<T> f)
    {
        try
        {
            return f();
        }
#pragma warning disable CA1031 // Detecção é best-effort.
        catch (Exception)
#pragma warning restore CA1031
        {
            return default;
        }
    }
}
