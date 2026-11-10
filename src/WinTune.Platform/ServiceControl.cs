using System.ComponentModel;
using System.Runtime.InteropServices;
using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>
/// Leitura de serviços via WMI/registro e alteração do tipo de inicialização via
/// <c>ChangeServiceConfig</c>. Nunca inicia nem para serviços.
/// </summary>
public sealed class ServiceControl : IServiceControl
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";
    private readonly IWmiReader _wmi;
    private readonly IRegistry _registry;

    /// <summary>Cria o controlador.</summary>
    public ServiceControl(IWmiReader wmi, IRegistry registry)
    {
        _wmi = wmi;
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<ServiceInfo> GetServices()
    {
        var rows = _wmi.Query(WmiScopes.Cimv2,
            "SELECT Name, DisplayName, State, StartMode, DelayedAutoStart, PathName, ProcessId FROM Win32_Service");
        return rows.Select(r => new ServiceInfo(
                r.GetString("Name") ?? string.Empty,
                r.GetString("DisplayName") ?? string.Empty,
                r.GetString("State") ?? "Unknown",
                new ServiceConfig(ParseWmiStartMode(r.GetString("StartMode")), r.GetBoolean("DelayedAutoStart") ?? false),
                r.GetString("PathName"),
                (int)(r.GetInt64("ProcessId") ?? 0)))
            .Where(s => s.Name.Length > 0)
            .ToList();
    }

    /// <inheritdoc />
    public ServiceConfig? GetConfig(string serviceName)
    {
        ValidateName(serviceName);
        var key = $@"{ServicesKey}\{serviceName}";
        var start = _registry.GetDWord(RegistryHive.LocalMachine, key, "Start");
        if (start is null)
        {
            return null;
        }

        var delayed = _registry.GetDWord(RegistryHive.LocalMachine, key, "DelayedAutostart") == 1;
        return new ServiceConfig(FromStartType(start.Value), start.Value == 2 && delayed);
    }

    /// <inheritdoc />
    public void SetConfig(string serviceName, ServiceConfig config)
    {
        ValidateName(serviceName);
        ArgumentNullException.ThrowIfNull(config);
        var startType = ToStartType(config.StartMode);

        using var scm = NativeMethods.OpenSCManagerW(null, null, NativeMethods.ScManagerConnect);
        if (scm.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using var svc = NativeMethods.OpenServiceW(scm, serviceName, NativeMethods.ServiceQueryConfig | NativeMethods.ServiceChangeConfig);
        if (svc.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (config.StartMode != ServiceStartMode.Automatic)
        {
            // Limpa o "atraso" enquanto ainda é automático; falha aqui é inofensiva.
            var info = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = false };
            NativeMethods.ChangeServiceConfig2W(svc, NativeMethods.ServiceConfigDelayedAutoStartInfo, ref info);
        }

        if (!NativeMethods.ChangeServiceConfigW(svc, NativeMethods.ServiceNoChange, startType, NativeMethods.ServiceNoChange,
                null, null, IntPtr.Zero, null, null, null, null))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (config.StartMode == ServiceStartMode.Automatic)
        {
            var info = new NativeMethods.ServiceDelayedAutoStartInfo { DelayedAutostart = config.DelayedAutoStart };
            if (!NativeMethods.ChangeServiceConfig2W(svc, NativeMethods.ServiceConfigDelayedAutoStartInfo, ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

    internal static ServiceStartMode ParseWmiStartMode(string? mode) => mode?.ToUpperInvariant() switch
    {
        "BOOT" => ServiceStartMode.Boot,
        "SYSTEM" => ServiceStartMode.System,
        "AUTO" => ServiceStartMode.Automatic,
        "MANUAL" => ServiceStartMode.Manual,
        "DISABLED" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };

    private static ServiceStartMode FromStartType(int start) => start switch
    {
        0 => ServiceStartMode.Boot,
        1 => ServiceStartMode.System,
        2 => ServiceStartMode.Automatic,
        3 => ServiceStartMode.Manual,
        4 => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };

    private static uint ToStartType(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => 2,
        ServiceStartMode.Manual => 3,
        ServiceStartMode.Disabled => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), "Somente Automático, Manual ou Desabilitado podem ser gravados."),
    };

    private static void ValidateName(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (serviceName.IndexOfAny(['\\', '/']) >= 0 || serviceName.Length > 256)
        {
            throw new ArgumentException("Nome de serviço inválido.", nameof(serviceName));
        }
    }

    private static class NativeMethods
    {
        public const uint ScManagerConnect = 0x0001;
        public const uint ServiceQueryConfig = 0x0001;
        public const uint ServiceChangeConfig = 0x0002;
        public const uint ServiceNoChange = 0xFFFFFFFF;
        public const uint ServiceConfigDelayedAutoStartInfo = 3;

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceDelayedAutoStartInfo
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool DelayedAutostart;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ScHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ScHandle OpenServiceW(ScHandle scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfigW(ScHandle service, uint serviceType, uint startType, uint errorControl,
            string? binaryPathName, string? loadOrderGroup, IntPtr tagId, string? dependencies,
            string? serviceStartName, string? password, string? displayName);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2W(ScHandle service, uint infoLevel, ref ServiceDelayedAutoStartInfo info);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr handle);
    }

    private sealed class ScHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
    }
}
