using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using Xunit;

namespace WinTune.Presentation.Tests;

public class StartupRecoveryTests
{
    [Fact]
    public async Task InterruptedApply_IsOfferedAndRevertedOnStartup()
    {
        var world = new TestWorld();
        world.Registry.With(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled", 1);

        // Simula queda: intenção + original gravados, 1º valor alterado, sem conclusão.
        world.Backup.BeginOperation("gamedvr-off", "apply");
        world.Backup.TryAddOriginal(new TweakBackup
        {
            TweakId = "gamedvr-off",
            Registry =
            [
                new RegistryBackupEntry
                {
                    Hive = RegistryHive.CurrentUser, KeyPath = TweakCatalog.GameConfigStoreKey, Name = "GameDVR_Enabled",
                    Original = StoredRegistryValue.From(RegistryValue.FromDWord(1)), Applied = StoredRegistryValue.From(RegistryValue.FromDWord(0)),
                },
                new RegistryBackupEntry
                {
                    Hive = RegistryHive.CurrentUser, KeyPath = TweakCatalog.GameDvrKey, Name = "AppCaptureEnabled",
                    Original = null, Applied = StoredRegistryValue.From(RegistryValue.FromDWord(0)),
                },
            ],
        });
        world.Registry.SetDWord(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled", 0);

        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(world.Services, dialogs);
        await vm.InitializeAsync();

        dialogs.Confirmations.Should().Contain(c => c.StartsWith("Operação interrompida", StringComparison.Ordinal));
        world.Registry.GetDWord(RegistryHive.CurrentUser, TweakCatalog.GameConfigStoreKey, "GameDVR_Enabled").Should().Be(1);
        world.Registry.GetValue(RegistryHive.CurrentUser, TweakCatalog.GameDvrKey, "AppCaptureEnabled").Should().BeNull();
        world.Backup.IncompleteOperations.Should().BeEmpty();
        vm.Log.Should().Contain(l => l.Contains("Recuperação"));
    }
}
