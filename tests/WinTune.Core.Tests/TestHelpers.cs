using WinTune.Core.Backup;
using WinTune.TestDoubles;

namespace WinTune.Core.Tests;

internal static class TestHelpers
{
    public const string BackupPath = @"C:\ProgramData\WinTune\backup.json";

    public static (BackupStore Store, FakeFileSystem Fs, FakeClock Clock) NewStore()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        return (BackupStore.Open(fs, clock, BackupPath), fs, clock);
    }
}
