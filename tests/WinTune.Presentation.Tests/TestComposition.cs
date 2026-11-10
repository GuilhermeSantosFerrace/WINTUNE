using WinTune.Core.Backup;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.Presentation;
using WinTune.TestDoubles;

namespace WinTune.Presentation.Tests;

internal sealed class FakeDialogs : IDialogService
{
    public Queue<bool> Answers { get; } = new();

    public bool DefaultAnswer { get; set; } = true;

    public List<string> Confirmations { get; } = [];

    public List<string> Infos { get; } = [];

    public bool Confirm(string title, string message)
    {
        Confirmations.Add(title + ": " + message);
        return Answers.Count > 0 ? Answers.Dequeue() : DefaultAnswer;
    }

    public void Inform(string title, string message) => Infos.Add(title + ": " + message);
}

/// <summary>WMI que bloqueia até ser liberado (prova de que a análise não roda na thread chamadora).</summary>
internal sealed class BlockingWmi(IWmiReader inner) : IWmiReader
{
    public ManualResetEventSlim Gate { get; } = new(false);

    public IReadOnlyList<WmiRow> Query(string scope, string wql)
    {
        Gate.Wait(TimeSpan.FromSeconds(10));
        return inner.Query(scope, wql);
    }
}

internal sealed class TestWorld
{
    public TestWorld(IWmiReader? wmi = null)
    {
        Registry = new FakeRegistry();
        Runner = new FakeProcessRunner().On(RestorePointService.PowerShellPath.Split('\\')[^1], ["-NoProfile"], string.Empty);
        Fs = new FakeFileSystem();
        Clock = new FakeClock();
        Backup = BackupStore.Open(Fs, Clock, @"C:\ProgramData\WinTune\backup.json");
        Wmi = wmi ?? new FakeWmiReader();
        Services = new AppServices
        {
            Registry = Registry,
            Runner = Runner,
            Wmi = Wmi,
            FileSystem = Fs,
            Environment = new FakeEnvironment(),
            Clock = Clock,
            Services = new FakeServiceControl(),
            Processes = new FakeProcessList(),
            Shell = new FakeShellLauncher(),
            Backup = Backup,
            Catalog = new TweakCatalog(Registry, Runner),
        };
    }

    public FakeRegistry Registry { get; }

    public FakeProcessRunner Runner { get; }

    public FakeFileSystem Fs { get; }

    public FakeClock Clock { get; }

    public BackupStore Backup { get; }

    public IWmiReader Wmi { get; }

    public AppServices Services { get; }
}
