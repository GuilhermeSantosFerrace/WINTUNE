using FluentAssertions;
using WinTune.Core.Startup;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Presentation.Tests;

public class StartupViewModelTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public async Task DisableIsOffByDefault_AndWorksWhenEnabled_ThenUndoAllRestores()
    {
        var world = new TestWorld();
        world.Registry.With(RegistryHive.CurrentUser, Run, "Steam", @"""C:\Steam\steam.exe"" -silent");
        var services = new AppServices
        {
            Registry = world.Registry,
            Runner = world.Runner,
            Wmi = world.Wmi,
            FileSystem = world.Fs,
            Environment = new FakeEnvironment(),
            Clock = world.Clock,
            Services = new FakeServiceControl(),
            Processes = new FakeProcessList(),
            Shell = new FakeShellLauncher(),
            Backup = world.Backup,
            Catalog = world.Services.Catalog,
            Startup = new StartupManager(world.Registry, world.Fs, new FakeEnvironment(), null),
        };
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(services, dialogs);
        var vm = main.Startup;

        vm.DisableSelectedCommand.CanExecute(null).Should().BeFalse("desativar é experimental e vem desligado");

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Should().ContainSingle(i => i.Name == "Steam");
        vm.AllowDisable = true;
        vm.Items[0].IsSelected = true;
        await vm.DisableSelectedCommand.ExecuteAsync(null);

        vm.Items.Single().State.Should().Be("Desabilitado (pelo WinTune)");

        await main.UndoAllCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Single().State.Should().Be("Habilitado");
        world.Backup.TweakIds.Should().BeEmpty();
    }
}
