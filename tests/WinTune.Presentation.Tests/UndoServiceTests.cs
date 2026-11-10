using FluentAssertions;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using Xunit;

namespace WinTune.Presentation.Tests;

public class UndoServiceTests
{
    [Fact]
    public void RevertAll_RestoresEverything_AndReportsSuccess()
    {
        var world = new TestWorld();
        world.Services.Catalog.Find("gamedvr-off")!.Apply(world.Backup);
        world.Services.Catalog.Find("startup-delay")!.Apply(world.Backup);

        var report = UndoService.RevertAll(world.Services);

        report.AllReverted.Should().BeTrue();
        world.Backup.TweakIds.Should().BeEmpty();
        world.Registry.GetValue(RegistryHive.CurrentUser, TweakCatalog.SerializeKey, "StartupDelayInMSec").Should().BeNull();
    }

    [Fact]
    public void RevertAll_NeverOverwritesDrift_AndReportsIt()
    {
        var world = new TestWorld();
        world.Services.Catalog.Find("startup-delay")!.Apply(world.Backup);
        world.Registry.SetDWord(RegistryHive.CurrentUser, TweakCatalog.SerializeKey, "StartupDelayInMSec", 777);

        var report = UndoService.RevertAll(world.Services);

        report.AllReverted.Should().BeFalse();
        report.Summary.Should().Contain("startup-delay").And.Contain("SAFETY.md");
        world.Registry.GetDWord(RegistryHive.CurrentUser, TweakCatalog.SerializeKey, "StartupDelayInMSec").Should().Be(777);
        world.Backup.HasBackup("startup-delay").Should().BeTrue("o backup é mantido para recuperação manual");
    }
}
