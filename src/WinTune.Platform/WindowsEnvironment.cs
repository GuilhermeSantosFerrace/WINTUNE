using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>Relógio do sistema.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Informações reais do ambiente Windows.</summary>
public sealed class WindowsEnvironment : IEnvironmentInfo
{
    /// <inheritdoc />
    public int OsBuild => Environment.OSVersion.Version.Build;

    /// <inheritdoc />
    public string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <inheritdoc />
    public string UserName => Environment.UserName;

    /// <inheritdoc />
    public string MachineName => Environment.MachineName;

    /// <inheritdoc />
    public string GetFolder(KnownFolder folder) => folder switch
    {
        KnownFolder.LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        KnownFolder.RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        KnownFolder.ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        KnownFolder.UserTemp => Path.GetTempPath(),
        KnownFolder.UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        KnownFolder.Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        KnownFolder.Startup => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        KnownFolder.CommonStartup => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        _ => throw new ArgumentOutOfRangeException(nameof(folder)),
    };
}
