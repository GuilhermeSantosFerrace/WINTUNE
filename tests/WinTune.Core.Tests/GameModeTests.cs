using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Games;
using WinTune.Core.Profiles;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class GameModeTests
{
    private sealed class World
    {
        public World()
        {
            Power = new PowerCfgSimulator(portuguese: true);
            Catalog = new TweakCatalog(Registry, Power.Runner);
            Backup = BackupStore.Open(Fs, Clock, TestHelpers.BackupPath);
            Store = new ProfileStore(Fs, new FakeEnvironment());
        }

        public FakeFileSystem Fs { get; } = new();

        public FakeClock Clock { get; } = new();

        public FakeRegistry Registry { get; } = new();

        public FakeProcessList Processes { get; } = new();

        public PowerCfgSimulator Power { get; }

        public TweakCatalog Catalog { get; }

        public BackupStore Backup { get; private set; }

        public ProfileStore Store { get; }

        public GameModeOrchestrator Orchestrator() =>
            new(Processes, GameDefinition.Defaults, Catalog.Find, Backup, Store, Clock);

        public void SimulateRestart() => Backup = BackupStore.Open(Fs, Clock, TestHelpers.BackupPath);
    }

    private static readonly GameModeSettings Enabled = new() { Enabled = true };

    [Fact]
    public void EnterAndExitGame_LeavesSystemInOriginalState()
    {
        var w = new World();
        var orch = w.Orchestrator();

        orch.Poll(Enabled).Should().BeEmpty("nenhum jogo aberto");

        w.Processes.Names.Add("cs2");
        orch.Poll(Enabled);
        w.Power.Active.Should().Be(PowerCfg.HighPerformanceGuid);
        orch.ActiveGame.Should().Be("cs2");

        orch.Poll(Enabled).Should().BeEmpty("continua no jogo, nada a fazer");

        w.Processes.Names.Clear();
        orch.Poll(Enabled);
        w.Power.Active.Should().Be(PowerCfgSimulator.Balanced);
        orch.ActiveGame.Should().BeNull();
        w.Backup.TweakIds.Should().BeEmpty();
    }

    [Fact]
    public void Disabled_DoesNothing()
    {
        var w = new World();
        w.Processes.Names.Add("RainbowSix");
        w.Orchestrator().Poll(new GameModeSettings { Enabled = false });
        w.Power.Active.Should().Be(PowerCfgSimulator.Balanced);
    }

    [Fact]
    public void TweakAlreadyAppliedByUser_IsNotTouched()
    {
        var w = new World();
        w.Catalog.Find("power-high")!.Apply(w.Backup);
        var orch = w.Orchestrator();

        w.Processes.Names.Add("cs2");
        orch.Poll(Enabled);
        w.Processes.Names.Clear();
        orch.Poll(Enabled);

        w.Power.Active.Should().Be(PowerCfg.HighPerformanceGuid, "o usuário aplicou manualmente; sair do jogo não desfaz");
        w.Backup.HasBackup("power-high").Should().BeTrue();
    }

    [Fact]
    public void CrashWhileInGame_IsDetectedAndRestoredOnNextStart()
    {
        var w = new World();
        w.Processes.Names.Add("RainbowSix.exe");
        w.Orchestrator().Poll(Enabled);
        w.Power.Active.Should().Be(PowerCfg.HighPerformanceGuid);

        // "Crash": o app some sem sair do modo jogo. Próxima abertura:
        w.SimulateRestart();
        var orch = w.Orchestrator();
        orch.ActiveGame.Should().Be("siege", "estado pendente detectado");

        orch.RestorePending();

        w.Power.Active.Should().Be(PowerCfgSimulator.Balanced);
        orch.ActiveGame.Should().BeNull();
    }

    [Fact]
    public void SwitchingGames_ExitsFirstThenEnters()
    {
        var w = new World();
        var orch = w.Orchestrator();
        w.Processes.Names.Add("cs2");
        orch.Poll(Enabled);
        w.Processes.Names.Clear();
        w.Processes.Names.Add("RainbowSix");

        orch.Poll(Enabled);

        orch.ActiveGame.Should().Be("siege");
        w.Power.Active.Should().Be(PowerCfg.HighPerformanceGuid);
    }

    [Fact]
    public void Profiles_RoundTrip_AndRejectBadNames()
    {
        var w = new World();
        w.Store.SaveProfile(new UserProfile { Name = "Jogar à noite", Focus = Focus.Gaming, Games = GameTarget.Cs2, TweakIds = ["gamedvr-off"] });

        w.Store.ListProfiles().Should().ContainSingle(p => p.Name == "Jogar à noite" && p.TweakIds.Contains("gamedvr-off"));
        var act = () => w.Store.SaveProfile(new UserProfile { Name = @"..\..\evil" });
        act.Should().Throw<ArgumentException>();
        w.Store.LoadSettings().Enabled.Should().BeFalse("modo jogo é opt-in");
    }

    [Fact]
    public void GamesJson_IsCreatedWithDefaults_AndInvalidFileFallsBackWithoutOverwrite()
    {
        var fs = new FakeFileSystem();
        var path = GamesConfigLoader.DefaultPath(new FakeEnvironment());

        var cfg = GamesConfigLoader.Load(fs, path);
        cfg.Games.Should().HaveCount(2);
        fs.Content(path).Should().Contain("RainbowSix.exe").And.Contain("cs2.exe");

        fs.AddFile(path, "{\"Schema\":1,\"Games\":[{\"Target\":\"Cs2\",\"ProcessNames\":[\"..\\\\x.exe\"],\"ExecutableRelativePath\":\"cs2.exe\"}]}");
        var bad = GamesConfigLoader.Load(fs, path);
        bad.Warning.Should().Contain("inválido");
        bad.Games.Should().HaveCount(2);
        fs.Content(path).Should().Contain(@"..\\x.exe", "o arquivo do usuário não é sobrescrito");
    }

    [Fact]
    public void GamesJson_UserCanEditProcessNames()
    {
        var doc = new GamesConfigDocument
        {
            Games = [new GameEntry { Target = GameTarget.RainbowSixSiege, ProcessNames = ["RainbowSix.exe", "RainbowSix_BE.exe"], ExecutableRelativePath = "RainbowSix.exe" }],
        };

        var cfg = GamesConfigLoader.Validate(doc);

        cfg.Games.Single().MatchesProcess("RainbowSix_BE").Should().BeTrue();
        cfg.Games.Single().DisplayName.Should().Be("Rainbow Six Siege");
    }
}
