using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class RegistryTweakTests
{
    private const string Key = @"Software\Test";

    private static RegistryTweak NewTweak(IRegistry reg) => new(reg,
        new TweakInfo { Id = "test-tweak", Name = "Teste", Description = "d", For = [Focus.Gaming] },
        [
            RegistrySetting.DWord(RegistryHive.CurrentUser, Key, "A", 1),
            RegistrySetting.DWord(RegistryHive.CurrentUser, Key, "B", 1),
        ]);

    [Fact]
    public void Apply_SavesBackupBeforeWriting_ThenRevertRestores()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, Key, "A", 0).With(RegistryHive.CurrentUser, Key, "B", 0);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);

        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        tweak.IsApplied().Should().BeTrue();
        store.Get("test-tweak")!.Registry.Should().HaveCount(2).And.OnlyContain(e => e.Original!.DWord == 0);

        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(0);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "B").Should().Be(0);
        store.HasBackup("test-tweak").Should().BeFalse();
    }

    [Fact]
    public void Revert_DeletesValues_ThatDidNotExist()
    {
        var reg = new FakeRegistry();
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);

        tweak.Apply(store);
        tweak.Revert(store);

        reg.GetValue(RegistryHive.CurrentUser, Key, "A").Should().BeNull();
        reg.GetValue(RegistryHive.CurrentUser, Key, "B").Should().BeNull();
    }

    [Fact]
    public void Reapply_DoesNotOverwriteOriginalBackup()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, Key, "A", 0).With(RegistryHive.CurrentUser, Key, "B", 0);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);

        tweak.Apply(store);
        tweak.Apply(store).Status.Should().Be(TweakStatus.Skipped);

        // Algo (ex.: Windows Update) desfaz um valor; reaplicar não pode trocar o original pelo estado otimizado.
        reg.SetDWord(RegistryHive.CurrentUser, Key, "A", 5);
        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);

        store.Get("test-tweak")!.Registry.Should().OnlyContain(e => e.Original!.DWord == 0);
    }

    [Fact]
    public void FailureOnSecondValue_RollsBackFirst()
    {
        var reg = new FakeRegistry { FailOnSet = (_, _, name) => name == "B" };
        reg.SetDWord(RegistryHive.CurrentUser, Key, "A", 0);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);

        var result = tweak.Apply(store);

        result.Status.Should().Be(TweakStatus.Failed);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(0);
        reg.GetValue(RegistryHive.CurrentUser, Key, "B").Should().BeNull();
        store.HasBackup("test-tweak").Should().BeFalse();
        store.IncompleteOperations.Should().BeEmpty();
    }

    [Fact]
    public void Revert_WithDrift_ReturnsConflict_AndDoesNotOverwrite()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, Key, "A", 0).With(RegistryHive.CurrentUser, Key, "B", 0);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);
        tweak.Apply(store);

        reg.SetDWord(RegistryHive.CurrentUser, Key, "A", 7);

        tweak.Revert(store).Status.Should().Be(TweakStatus.Conflict);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(7);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "B").Should().Be(1);
        store.HasBackup("test-tweak").Should().BeTrue();

        tweak.Revert(store, RevertMode.Force).Status.Should().Be(TweakStatus.Success);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(0);
    }

    [Fact]
    public void Revert_IgnoresBackupEntriesOutsideAllowlist()
    {
        var reg = new FakeRegistry().With(RegistryHive.LocalMachine, @"SYSTEM\Critical", "X", 1);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = NewTweak(reg);
        tweak.Apply(store);

        // Simula adulteração: entrada injetada apontando para outra chave.
        store.MergeRegistry("test-tweak",
        [
            new RegistryBackupEntry
            {
                Hive = RegistryHive.LocalMachine, KeyPath = @"SYSTEM\Critical", Name = "X",
                Original = StoredRegistryValue.From(RegistryValue.FromDWord(0)),
                Applied = StoredRegistryValue.From(RegistryValue.FromDWord(1)),
            },
        ]);

        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);
        reg.GetDWord(RegistryHive.LocalMachine, @"SYSTEM\Critical", "X").Should().Be(1);
        store.JournalEntries.Should().Contain(j => j.Result == "ignored-outside-allowlist");
    }

    [Fact]
    public void DescribeChanges_DoesNotWrite()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, Key, "A", 0);
        var writes = reg.WriteCount;

        var lines = NewTweak(reg).DescribeChanges();

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("DWORD 0").And.Contain("DWORD 1");
        reg.WriteCount.Should().Be(writes);
    }
}
