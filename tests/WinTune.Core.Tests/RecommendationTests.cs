using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Recommendations;
using WinTune.Core.Tweaks;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class RecommendationTests
{
    private sealed class StubTweak(string id, Focus[] @for, Scenario helps, GameTarget games = GameTarget.None, int priority = 50,
        bool applied = false, bool defaultSelected = true) : Tweak
    {
        public override string Id => id;

        public override string Name => id;

        public override string Description => id;

        public override Focus[] For => @for;

        public override Scenario Helps => helps;

        public override GameTarget Games => games;

        public override int Priority => priority;

        public override bool DefaultSelected => defaultSelected;

        public override string Mechanism => "-";

        public override bool IsApplied() => applied;

        public override IReadOnlyList<string> DescribeChanges() => [];

        public override TweakResult Apply(BackupStore store) => TweakResult.Success(id, "ok");

        public override bool CanRevert(BackupStore store) => false;

        public override TweakResult Revert(BackupStore store, RevertMode mode) => TweakResult.Skipped(id, "-");
    }

    private static readonly StubTweak GamingStutter = new("gaming-stutter", [Focus.Gaming], Scenario.Stutter);
    private static readonly StubTweak WorkAny = new("work-any", [Focus.Work], Scenario.None);
    private static readonly StubTweak BothBoot = new("both-boot", [Focus.Work, Focus.Gaming], Scenario.SlowBoot);
    private static readonly StubTweak SiegeOnly = new("siege-only", [Focus.Gaming], Scenario.None, GameTarget.RainbowSixSiege);
    private static readonly StubTweak Cs2Only = new("cs2-only", [Focus.Gaming], Scenario.None, GameTarget.Cs2);

    public static TheoryData<Focus, Scenario, GameTarget, string[]> Matrix => new()
    {
        // Foco × cenários (sem jogos)
        { Focus.Work, Scenario.None, GameTarget.None, ["work-any", "both-boot"] },
        { Focus.Gaming, Scenario.None, GameTarget.None, ["gaming-stutter", "both-boot", "siege-only", "cs2-only"] },
        { Focus.Both, Scenario.None, GameTarget.None, ["gaming-stutter", "work-any", "both-boot", "siege-only", "cs2-only"] },
        { Focus.Gaming, Scenario.Stutter, GameTarget.None, ["gaming-stutter", "siege-only", "cs2-only"] },
        { Focus.Gaming, Scenario.SlowBoot, GameTarget.None, ["both-boot", "siege-only", "cs2-only"] },
        { Focus.Work, Scenario.SlowBoot, GameTarget.None, ["work-any", "both-boot"] },
        { Focus.Both, Scenario.Stutter | Scenario.SlowBoot, GameTarget.None, ["gaming-stutter", "work-any", "both-boot", "siege-only", "cs2-only"] },

        // Jogos
        { Focus.Gaming, Scenario.None, GameTarget.RainbowSixSiege, ["gaming-stutter", "both-boot", "siege-only"] },
        { Focus.Gaming, Scenario.None, GameTarget.Cs2, ["gaming-stutter", "both-boot", "cs2-only"] },
        { Focus.Gaming, Scenario.None, GameTarget.RainbowSixSiege | GameTarget.Cs2, ["gaming-stutter", "both-boot", "siege-only", "cs2-only"] },
        { Focus.Both, Scenario.None, GameTarget.Cs2, ["gaming-stutter", "work-any", "both-boot", "cs2-only"] },

        // Foco Trabalho ignora jogos
        { Focus.Work, Scenario.None, GameTarget.RainbowSixSiege, ["work-any", "both-boot"] },
        { Focus.Work, Scenario.None, GameTarget.Cs2, ["work-any", "both-boot"] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Recommend_FiltersByFocusScenarioAndGame(Focus focus, Scenario scenarios, GameTarget games, string[] expected)
    {
        var tweaks = new Tweak[] { GamingStutter, WorkAny, BothBoot, SiegeOnly, Cs2Only };

        var ids = new RecommendationEngine()
            .Recommend(tweaks, new RecommendationRequest(focus, scenarios, games, SystemProfile.Unknown))
            .Select(r => r.Tweak.Id);

        ids.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Recommend_SortsByPriority_AndUnselectsAppliedOrNonDefault()
    {
        var tweaks = new Tweak[]
        {
            new StubTweak("low", [Focus.Gaming], Scenario.None, priority: 10),
            new StubTweak("high", [Focus.Gaming], Scenario.None, priority: 90),
            new StubTweak("applied", [Focus.Gaming], Scenario.None, priority: 50, applied: true),
            new StubTweak("optin", [Focus.Gaming], Scenario.None, priority: 40, defaultSelected: false),
        };

        var recs = new RecommendationEngine().Recommend(tweaks, new RecommendationRequest(Focus.Gaming, Scenario.None, GameTarget.None, SystemProfile.Unknown));

        recs.Select(r => r.Tweak.Id).Should().Equal("high", "applied", "optin", "low");
        recs.Single(r => r.Tweak.Id == "applied").Should().Match<Recommendation>(r => !r.Selected && r.AlreadyApplied && r.Note!.Contains("nada a fazer"));
        recs.Single(r => r.Tweak.Id == "optin").Selected.Should().BeFalse();
        recs.Single(r => r.Tweak.Id == "high").Selected.Should().BeTrue();
    }
}

public class CatalogTests
{
    [Fact]
    public void AllTweaks_HaveUniqueKebabIds_AndPortugueseDescriptions()
    {
        var catalog = new TweakCatalog(new FakeRegistry(), new FakeProcessRunner());

        catalog.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        catalog.All.Should().OnlyContain(t => System.Text.RegularExpressions.Regex.IsMatch(t.Id, "^[a-z0-9]+(-[a-z0-9]+)*$"));
        catalog.All.Should().OnlyContain(t => t.Description.Length > 40 && t.Mechanism.Length > 0);
    }

    [Fact]
    public void EveryTweak_IsDocumentedInTweaksMd_WithSource()
    {
        var doc = File.ReadAllText(Path.Combine(ArchitectureTests.RepoRoot(), "docs", "TWEAKS.md"));
        var catalog = new TweakCatalog(new FakeRegistry(), new FakeProcessRunner());

        foreach (var t in catalog.All)
        {
            var start = doc.IndexOf($"### `{t.Id}`", StringComparison.Ordinal);
            start.Should().BeGreaterThan(-1, $"{t.Id} precisa estar em TWEAKS.md");
            var next = doc.IndexOf("\n### ", start + 5, StringComparison.Ordinal);
            var section = next < 0 ? doc[start..] : doc[start..next];
            section.Should().Contain("**Fonte:**", $"{t.Id} precisa citar a fonte");
            section.Should().Contain("**Reverter sem o app:**", $"{t.Id} precisa do rollback manual");
        }
    }

    [Fact]
    public void Hags_IsOptIn_AndReportsAlreadyActive()
    {
        var reg = new FakeRegistry().With(Platform.Abstractions.RegistryHive.LocalMachine, TweakCatalog.GraphicsDriversKey, "HwSchMode", 2);
        var hags = new TweakCatalog(reg, new FakeProcessRunner()).Find("hags")!;

        hags.DefaultSelected.Should().BeFalse();
        hags.RequiresReboot.Should().BeTrue();
        hags.GetAdvice(SystemProfile.Unknown).Note.Should().Contain("já está ativo");
    }

    [Fact]
    public void GameDvrOff_AppliesAndRevertsBothValues()
    {
        var reg = new FakeRegistry()
            .With(Platform.Abstractions.RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled", 1);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new TweakCatalog(reg, new FakeProcessRunner()).Find("gamedvr-off")!;

        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);

        reg.GetValue(Platform.Abstractions.RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled")!.DWord.Should().Be(1);
        reg.GetValue(Platform.Abstractions.RegistryHive.CurrentUser, TweakCatalog.GameDvrKey, "AppCaptureEnabled").Should().BeNull();
    }
}
