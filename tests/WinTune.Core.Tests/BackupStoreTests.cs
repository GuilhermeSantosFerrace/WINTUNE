using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class BackupStoreTests
{
    [Fact]
    public void TryAddOriginal_PersistsAtomically_AndNeverOverwrites()
    {
        var (store, fs, clock) = TestHelpers.NewStore();
        var first = new TweakBackup
        {
            TweakId = "game-mode",
            Registry = [new RegistryBackupEntry { Hive = RegistryHive.CurrentUser, KeyPath = "K", Name = "V", Original = StoredRegistryValue.From(RegistryValue.FromDWord(0)) }],
        };
        var second = new TweakBackup
        {
            TweakId = "game-mode",
            Registry = [new RegistryBackupEntry { Hive = RegistryHive.CurrentUser, KeyPath = "K", Name = "V", Original = StoredRegistryValue.From(RegistryValue.FromDWord(1)) }],
        };

        store.TryAddOriginal(first).Should().BeTrue();
        store.TryAddOriginal(second).Should().BeFalse();

        var reopened = BackupStore.Open(fs, clock, TestHelpers.BackupPath);
        reopened.Get("game-mode")!.Registry[0].Original!.DWord.Should().Be(0);
        fs.Content(TestHelpers.BackupPath).Should().Contain("\"Schema\": 1");
    }

    [Fact]
    public void CorruptFile_IsMovedAside_AndStoreStartsEmptyWithWarning()
    {
        var fs = new FakeFileSystem().AddFile(TestHelpers.BackupPath, "{ isto não é json");
        var clock = new FakeClock();

        var store = BackupStore.Open(fs, clock, TestHelpers.BackupPath);

        store.LoadWarning.Should().Contain(".corrupt");
        store.TweakIds.Should().BeEmpty();
        fs.FileExists(TestHelpers.BackupPath + ".corrupt-20261110120000").Should().BeTrue();
    }

    [Fact]
    public void UnsupportedSchema_IsTreatedAsCorrupt()
    {
        var fs = new FakeFileSystem().AddFile(TestHelpers.BackupPath, "{\"Schema\": 99}");
        var store = BackupStore.Open(fs, new FakeClock(), TestHelpers.BackupPath);
        store.LoadWarning.Should().NotBeNull();
    }

    [Fact]
    public void InvalidTweakIdInFile_IsTreatedAsCorrupt()
    {
        var fs = new FakeFileSystem().AddFile(TestHelpers.BackupPath,
            "{\"Schema\":1,\"Entries\":{\"..\\\\evil\":{\"TweakId\":\"..\\\\evil\"}}}");
        var store = BackupStore.Open(fs, new FakeClock(), TestHelpers.BackupPath);
        store.LoadWarning.Should().NotBeNull();
        store.TweakIds.Should().BeEmpty();
    }

    [Fact]
    public void OversizedFile_IsTreatedAsCorrupt()
    {
        var fs = new FakeFileSystem().AddFile(TestHelpers.BackupPath, "{}", length: BackupStore.MaxFileBytes + 1);
        BackupStore.Open(fs, new FakeClock(), TestHelpers.BackupPath).LoadWarning.Should().NotBeNull();
    }

    [Fact]
    public void Operations_TrackIncompleteIntents()
    {
        var (store, fs, clock) = TestHelpers.NewStore();
        var op = store.BeginOperation("hags", "apply");

        BackupStore.Open(fs, clock, TestHelpers.BackupPath).IncompleteOperations.Should().ContainSingle(o => o.TweakId == "hags");

        store.CompleteOperation(op);
        BackupStore.Open(fs, clock, TestHelpers.BackupPath).IncompleteOperations.Should().BeEmpty();
    }

    [Fact]
    public void Journal_RecordsEntries_AndIsCapped()
    {
        var (store, _, _) = TestHelpers.NewStore();
        for (var i = 0; i < BackupStore.MaxJournalEntries + 10; i++)
        {
            store.Journal("apply", "x", "success");
        }

        store.JournalEntries.Should().HaveCount(BackupStore.MaxJournalEntries);
    }

    [Fact]
    public void SetExtra_DoesNotOverwriteByDefault()
    {
        var (store, _, _) = TestHelpers.NewStore();
        store.TryAddOriginal(new TweakBackup { TweakId = "power-high" });
        store.SetExtra("power-high", "previousScheme", "a");
        store.SetExtra("power-high", "previousScheme", "b");
        store.Get("power-high")!.Extras["previousScheme"].Should().Be("a");
    }
}

public class RestorePointServiceTests
{
    [Fact]
    public void Success_IsCreated_AndJournaled()
    {
        var (store, _, _) = TestHelpers.NewStore();
        var runner = new FakeProcessRunner().On("powershell.exe", ["-NoProfile"], string.Empty);

        var result = new RestorePointService(runner, store).TryCreate();

        result.Status.Should().Be(RestorePointStatus.Created);
        store.JournalEntries.Should().ContainSingle(j => j.Action == "restore-point");
        runner.Calls[0].Arguments.Should().Contain(RestorePointService.Command);
    }

    [Fact]
    public void FrequencyWarning_IsSkipped_NotFailed()
    {
        var (store, _, _) = TestHelpers.NewStore();
        var runner = new FakeProcessRunner().On("powershell.exe", ["-NoProfile"],
            "AVISO: Não é possível criar um novo ponto de restauração do sistema porque um já foi criado nos últimos 1440 minutos.");

        new RestorePointService(runner, store).TryCreate().Status.Should().Be(RestorePointStatus.SkippedFrequencyLimit);
    }

    [Fact]
    public void Failure_IsTolerated()
    {
        var (store, _, _) = TestHelpers.NewStore();
        var runner = new FakeProcessRunner().On("powershell.exe", ["-NoProfile"],
            _ => new ProcessResult(1, string.Empty, "A proteção do sistema está desligada", false));

        var result = new RestorePointService(runner, store).TryCreate();

        result.Status.Should().Be(RestorePointStatus.Failed);
        result.Message.Should().Contain("Proteção do Sistema");
    }
}
