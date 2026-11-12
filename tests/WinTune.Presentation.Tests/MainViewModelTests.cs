using FluentAssertions;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Presentation.Tests;

public class MainViewModelTests
{
    [Theory]
    [InlineData(Focus.Work, false)]
    [InlineData(Focus.Gaming, true)]
    [InlineData(Focus.Both, true)]
    [InlineData(Focus.Streaming, true)]
    public void GamesSection_VisibleOnlyWhenFocusIncludesGaming(Focus focus, bool visible)
    {
        var vm = new MainViewModel(new TestWorld().Services, new FakeDialogs()) { SelectedFocus = focus };
        vm.IsGamesSectionVisible.Should().Be(visible);
    }

    [Fact]
    public void StreamDropsScenario_OnlyAvailableAndEffectiveInStreamingFocus()
    {
        var vm = new MainViewModel(new TestWorld().Services, new FakeDialogs());
        var drops = vm.Scenarios.Single(s => s.Scenario == Scenario.StreamDrops);
        drops.IsChecked = true;

        drops.IsAvailable.Should().BeFalse();
        vm.SelectedScenarios.Should().Be(Scenario.None, "fora do foco Jogos + live a opção é ignorada");

        vm.IsStreamingFocus = true;
        drops.IsAvailable.Should().BeTrue();
        vm.SelectedScenarios.Should().Be(Scenario.StreamDrops);
        vm.IsGamingFocus.Should().BeFalse();

        vm.IsGamingFocus = true;
        drops.IsAvailable.Should().BeFalse();
        vm.SelectedScenarios.Should().Be(Scenario.None);
    }

    [Fact]
    public async Task Analyze_InStreamingFocus_FillsLiveFindings_OtherFociDoNot()
    {
        var world = new TestWorld();
        var vm = new MainViewModel(world.Services, new FakeDialogs()) { SelectedFocus = Focus.Gaming };
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.StreamingFindings.Should().BeEmpty();

        vm.IsStreamingFocus = true;
        await vm.AnalyzeCommand.ExecuteAsync(null);

        vm.StreamingFindings.Should().Contain(f => f.Finding.Id == "streaming-guide-obs");
        vm.GameFindings.Should().NotBeEmpty("o foco Jogos + live também faz as verificações de jogos");

        vm.IsWorkFocus = true;
        await vm.AnalyzeCommand.ExecuteAsync(null);
        vm.StreamingFindings.Should().BeEmpty();
    }

    [Fact]
    public void WorkFocus_IgnoresGameSelection()
    {
        var vm = new MainViewModel(new TestWorld().Services, new FakeDialogs()) { SiegeSelected = true, Cs2Selected = true };
        vm.SelectedGames.Should().Be(GameTarget.RainbowSixSiege | GameTarget.Cs2);
        vm.IsWorkFocus = true;
        vm.SelectedGames.Should().Be(GameTarget.None);
    }

    [Fact]
    public void RadioProperties_DriveSelectedFocus()
    {
        var vm = new MainViewModel(new TestWorld().Services, new FakeDialogs());
        vm.IsBothFocus = true;
        vm.SelectedFocus.Should().Be(Focus.Both);
        vm.IsGamingFocus.Should().BeFalse();
    }

    [Fact]
    public async Task Recommendations_FollowFocusAndScenarios()
    {
        var vm = new MainViewModel(new TestWorld().Services, new FakeDialogs()) { SelectedFocus = Focus.Work };
        await vm.RefreshRecommendationsAsync();
        vm.Recommendations.Select(r => r.Id).Should().BeEquivalentTo("startup-delay", "transparency-off");

        vm.SelectedFocus = Focus.Gaming;
        vm.Scenarios.Single(s => s.Scenario == Scenario.SlowBoot).IsChecked = true;
        await vm.RefreshRecommendationsAsync();
        vm.Recommendations.Select(r => r.Id).Should().BeEquivalentTo("startup-delay");
    }

    [Fact]
    public async Task Analyze_DoesNotRunOnCallingThread_AndPopulatesFindings()
    {
        var wmi = new BlockingWmi(new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", 8UL * 1024 * 1024 * 1024)));
        var vm = new MainViewModel(new TestWorld(wmi).Services, new FakeDialogs()) { SiegeSelected = true };

        var task = vm.AnalyzeCommand.ExecuteAsync(null);

        task.IsCompleted.Should().BeFalse("a análise deve rodar em segundo plano, sem bloquear quem chamou");
        vm.State.Should().Be(AppState.Analyzing);
        vm.ApplySelectedCommand.CanExecute(null).Should().BeFalse();

        wmi.Gate.Set();
        await task;

        vm.State.Should().Be(AppState.Idle);
        vm.HasAnalyzed.Should().BeTrue();
        vm.SystemFindings.Should().Contain(f => f.Finding.Id == "ram-single-channel");
        vm.GameFindings.Should().Contain(f => f.Finding.Id == "game-guide-siege");
    }

    [Fact]
    public async Task Analyze_CanBeCancelled()
    {
        var wmi = new BlockingWmi(new FakeWmiReader());
        var vm = new MainViewModel(new TestWorld(wmi).Services, new FakeDialogs());

        var task = vm.AnalyzeCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        wmi.Gate.Set();
        await task;

        vm.State.Should().Be(AppState.Idle);
        vm.StatusMessage.Should().Contain("cancelada");
    }

    [Fact]
    public async Task ApplySelected_ConfirmsThenAppliesAndLogs()
    {
        var world = new TestWorld();
        world.Registry.With(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled", 1);
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(world.Services, dialogs) { SelectedFocus = Focus.Gaming };
        await vm.RefreshRecommendationsAsync();
        foreach (var r in vm.Recommendations)
        {
            r.IsSelected = r.Id == "gamedvr-off";
        }

        await vm.ApplySelectedCommand.ExecuteAsync(null);

        dialogs.Confirmations.Should().ContainSingle(c => c.Contains("GameDVR_Enabled") && c.Contains("DWORD 1 → DWORD 0"));
        world.Registry.GetDWord(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().Be(0);
        world.Backup.HasBackup("gamedvr-off").Should().BeTrue();
        world.Runner.Calls.Should().Contain(c => c.Executable.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase));
        vm.Log.Should().Contain(l => l.Contains("Sucesso"));
        vm.Recommendations.Single(r => r.Id == "gamedvr-off").AlreadyApplied.Should().BeTrue();
    }

    [Fact]
    public async Task ApplySelected_DeclinedConfirmation_WritesNothing()
    {
        var world = new TestWorld();
        var dialogs = new FakeDialogs { DefaultAnswer = false };
        var vm = new MainViewModel(world.Services, dialogs) { SelectedFocus = Focus.Gaming };
        await vm.RefreshRecommendationsAsync();
        var writes = world.Registry.WriteCount;

        await vm.ApplySelectedCommand.ExecuteAsync(null);

        world.Registry.WriteCount.Should().Be(writes);
        world.Backup.TweakIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UndoAll_RevertsEverything_AndAsksOnConflict()
    {
        var world = new TestWorld();
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(world.Services, dialogs) { SelectedFocus = Focus.Both };
        await vm.RefreshRecommendationsAsync();
        foreach (var r in vm.Recommendations)
        {
            r.IsSelected = r.Id is "gamedvr-off" or "startup-delay";
        }

        await vm.ApplySelectedCommand.ExecuteAsync(null);
        world.Backup.TweakIds.Should().BeEquivalentTo("gamedvr-off", "startup-delay");

        // Outro programa mexe em um valor depois do WinTune.
        world.Registry.SetDWord(RegistryHive.CurrentUser, TweakCatalog.SerializeKey, "StartupDelayInMSec", 500);
        dialogs.Answers.Enqueue(true); // confirma "Desfazer tudo"
        dialogs.Answers.Enqueue(true); // confirma restaurar mesmo com conflito

        await vm.UndoAllCommand.ExecuteAsync(null);

        world.Backup.TweakIds.Should().BeEmpty();
        world.Registry.GetValue(RegistryHive.CurrentUser, TweakCatalog.SerializeKey, "StartupDelayInMSec").Should().BeNull();
        dialogs.Confirmations.Should().Contain(c => c.StartsWith("Valor alterado por outro programa", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UndoAll_NothingToUndo_Informs()
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(new TestWorld().Services, dialogs);
        await vm.UndoAllCommand.ExecuteAsync(null);
        dialogs.Infos.Should().ContainSingle(i => i.Contains("Não há ajustes"));
    }
}
