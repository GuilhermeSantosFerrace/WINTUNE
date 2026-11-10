using FluentAssertions;
using WinTune.Core.Domain;
using WinTune.Core.Startup;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class StartupTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static (StartupManager Manager, FakeRegistry Reg, FakeFileSystem Fs) World()
    {
        var reg = new FakeRegistry()
            .With(RegistryHive.CurrentUser, Run, "Discord", @"""C:\Users\fulano\AppData\Local\Discord\Update.exe"" --processStart Discord.exe")
            .With(RegistryHive.CurrentUser, Run, "OneDrive", @"""C:\Program Files\Microsoft OneDrive\OneDrive.exe"" /background")
            .With(RegistryHive.LocalMachine, Run, "SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe")
            .With(RegistryHive.LocalMachine, Run, "RtkAudUService", @"C:\Windows\System32\RtkAudUService64.exe -background");
        var fs = new FakeFileSystem()
            .AddFile(@"C:\Users\fulano\AppData\Local\Discord\Update.exe", length: 2_000_000, company: "Discord Inc.", signer: "CN=Discord Inc., O=Discord Inc.")
            .AddFile(@"C:\Program Files\Microsoft OneDrive\OneDrive.exe", company: "Microsoft Corporation", signer: "CN=Microsoft Corporation, O=Microsoft Corporation")
            .AddFile(@"C:\Windows\System32\RtkAudUService64.exe", company: "Realtek Semiconductor")
            .AddFile(@"C:\Users\fulano\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\Spotify.lnk");
        var tasks = new FakeScheduledTasks().Add(@"\Updater At Logon", logon: true, actions: @"C:\Tools\upd.exe").Add(@"\Daily", logon: false);
        return (new StartupManager(reg, fs, new FakeEnvironment(), tasks), reg, fs);
    }

    [Fact]
    public void List_FindsAllSources_WithPublisherImpactAndProtection()
    {
        var (manager, _, _) = World();

        var items = manager.List();

        items.Should().Contain(i => i.Name == "Discord" && i.Source == StartupSource.RunUser && i.CanDisable && i.Impact == "Alto" && i.Publisher == "Discord Inc.");
        items.Should().Contain(i => i.Name == "OneDrive" && i.BlockedReason!.Contains("Microsoft"));
        items.Should().Contain(i => i.Name == "SecurityHealth" && i.BlockedReason != null);
        items.Should().Contain(i => i.Name == "RtkAudUService" && i.BlockedReason!.Contains("hardware"));
        items.Should().Contain(i => i.Name == "Spotify.lnk" && i.Source == StartupSource.StartupFolderUser && i.Impact == "Alto");
        items.Should().ContainSingle(i => i.Source == StartupSource.ScheduledTaskLogon && i.BlockedReason != null);
    }

    [Fact]
    public void DisableThenReenable_UsesStartupApproved_AndRestoresOriginalState()
    {
        var (manager, reg, _) = World();
        var (store, _, _) = TestHelpers.NewStore();
        var discord = manager.List().Single(i => i.Name == "Discord");

        var tweak = manager.CreateDisableTweak(discord)!;
        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);

        var value = reg.GetValue(RegistryHive.CurrentUser, ApprovedRun, "Discord")!;
        value.Binary![0].Should().Be(0x03);
        value.Binary.Should().HaveCount(12);
        manager.List().Single(i => i.Name == "Discord").State.Should().Be(StartupState.Disabled);
        reg.GetString(RegistryHive.CurrentUser, Run, "Discord").Should().NotBeNull("o item nunca é removido");

        manager.Resolve(discord.Id)!.Revert(store).Status.Should().Be(TweakStatus.Success);
        reg.GetValue(RegistryHive.CurrentUser, ApprovedRun, "Discord").Should().BeNull();
        manager.List().Single(i => i.Name == "Discord").State.Should().Be(StartupState.Enabled);
    }

    [Fact]
    public void Reenable_RestoresPreviousEnabledBinaryExactly()
    {
        var (manager, reg, _) = World();
        var enabled = new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        reg.SetValue(RegistryHive.CurrentUser, ApprovedRun, "Discord", RegistryValue.FromBinary(enabled));
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = manager.CreateDisableTweak(manager.List().Single(i => i.Name == "Discord"))!;

        tweak.Apply(store);
        tweak.Revert(store);

        reg.GetValue(RegistryHive.CurrentUser, ApprovedRun, "Discord")!.Binary.Should().Equal(enabled);
    }

    [Fact]
    public void ProtectedItems_CannotBeDisabled()
    {
        var (manager, _, _) = World();
        manager.CreateDisableTweak(manager.List().Single(i => i.Name == "OneDrive")).Should().BeNull();
        manager.CreateDisableTweak(manager.List().Single(i => i.Source == StartupSource.ScheduledTaskLogon)).Should().BeNull();
    }

    [Fact]
    public void Resolve_FindsOrphanApprovedEntries_ButIgnoresUnknownIds()
    {
        var (manager, reg, _) = World();
        reg.SetValue(RegistryHive.CurrentUser, ApprovedRun, "Removido", RegistryValue.FromBinary(StartupDisableTweak.DisabledValue(DateTime.UtcNow)));

        manager.Resolve(StartupManager.MakeId(StartupSource.RunUser, "Removido")).Should().NotBeNull();
        manager.Resolve("startup-item:runuser-000000000000").Should().BeNull();
        manager.Resolve("game-mode").Should().BeNull();
    }

    [Theory]
    [InlineData(@"""C:\A B\app.exe"" -x", @"C:\A B\app.exe")]
    [InlineData(@"C:\Tools\app.exe /min", @"C:\Tools\app.exe")]
    [InlineData("", null)]
    public void ExtractExecutable_HandlesQuotesAndArgs(string command, string? expected)
    {
        StartupManager.ExtractExecutable(command).Should().Be(expected);
    }

    [Fact]
    public void ParseApproved_HandlesKnownAndUnknownValues()
    {
        StartupManager.ParseApproved(null).Should().Be(StartupState.Enabled);
        StartupManager.ParseApproved(RegistryValue.FromBinary([0x02, 0])).Should().Be(StartupState.Enabled);
        StartupManager.ParseApproved(RegistryValue.FromBinary([0x03, 0])).Should().Be(StartupState.Disabled);
        StartupManager.ParseApproved(RegistryValue.FromBinary([0x09, 0])).Should().Be(StartupState.Unknown);
        StartupManager.ParseApproved(RegistryValue.FromDWord(1)).Should().Be(StartupState.Unknown);
    }
}
