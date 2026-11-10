using FluentAssertions;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using Xunit;

namespace WinTune.Presentation.Tests;

public class ProfilesViewModelTests
{
    [Fact]
    public async Task SaveAndLoadProfile_RestoresScreenState()
    {
        var world = new TestWorld();
        var main = new MainViewModel(world.Services, new FakeDialogs()) { SelectedFocus = Focus.Gaming, Cs2Selected = true };
        main.Scenarios.Single(s => s.Scenario == Scenario.Stutter).IsChecked = true;
        await main.RefreshRecommendationsAsync();
        foreach (var r in main.Recommendations)
        {
            r.IsSelected = r.Id == "gamedvr-off";
        }

        main.Profiles.NewProfileName = "CS2 competitivo";
        main.Profiles.SaveCurrentCommand.Execute(null);
        main.Profiles.Profiles.Should().ContainSingle(p => p.Name == "CS2 competitivo");

        main.SelectedFocus = Focus.Work;
        main.Cs2Selected = false;
        main.Profiles.SelectedProfile = main.Profiles.Profiles.Single();
        await main.Profiles.LoadSelectedCommand.ExecuteAsync(null);

        main.SelectedFocus.Should().Be(Focus.Gaming);
        main.Cs2Selected.Should().BeTrue();
        main.Scenarios.Single(s => s.Scenario == Scenario.Stutter).IsChecked.Should().BeTrue();
        main.Recommendations.Where(r => r.IsSelected).Select(r => r.Id).Should().Equal("gamedvr-off");
    }

    [Fact]
    public async Task GameMode_IsOptIn_AppliesOnGameAndRestoresOnExit()
    {
        var world = new TestWorld();
        var processes = (WinTune.TestDoubles.FakeProcessList)world.Services.Processes;
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(world.Services, dialogs);
        var vm = main.Profiles;
        vm.IsGameModeEnabled.Should().BeFalse();
        vm.PollInterval = TimeSpan.FromHours(1);

        // Usa um ajuste de registro para o teste (game DVR) no perfil do CS2.
        foreach (var o in vm.Cs2Tweaks)
        {
            o.IsChecked = o.Id == "gamedvr-off";
        }

        bool? trayVisible = null;
        vm.GameModeEnabledChanged += (_, on) => trayVisible = on;
        vm.IsGameModeEnabled = true;
        trayVisible.Should().BeTrue();

        processes.Names.Add("cs2");
        await vm.PollOnceAsync();
        world.Registry.GetDWord(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().Be(0);
        vm.GameModeStatus.Should().Contain("ATIVO");

        processes.Names.Clear();
        await vm.PollOnceAsync();
        world.Registry.GetValue(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().BeNull();
        world.Backup.TweakIds.Should().BeEmpty();

        vm.IsGameModeEnabled = false;
        trayVisible.Should().BeFalse();
    }

    [Fact]
    public async Task PendingStateAfterCrash_IsOfferedOnStartup()
    {
        var world = new TestWorld();
        var processes = (WinTune.TestDoubles.FakeProcessList)world.Services.Processes;
        var first = new MainViewModel(world.Services, new FakeDialogs()).Profiles;
        foreach (var o in first.SiegeTweaks)
        {
            o.IsChecked = o.Id == "gamedvr-off";
        }

        first.PollInterval = TimeSpan.FromHours(1);
        first.IsGameModeEnabled = true;
        processes.Names.Add("RainbowSix");
        await first.PollOnceAsync();
        world.Registry.GetDWord(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().Be(0);

        // "Crash" e nova abertura com o jogo já fechado.
        processes.Names.Clear();
        var dialogs = new FakeDialogs();
        var second = new MainViewModel(world.Services, dialogs).Profiles;
        await second.InitializeAsync();

        dialogs.Confirmations.Should().Contain(c => c.StartsWith("Modo jogo pendente", StringComparison.Ordinal));
        world.Registry.GetValue(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().BeNull();
    }
}
