using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

/// <summary>R13 / Fase 14: o processo morre no meio de Apply; o próximo início detecta e reverte o estado parcial.</summary>
public class ResilienceTests
{
    private sealed class ProcessKilledException : Exception;

    private const string Key = @"Software\Test";

    private static RegistryTweak NewTweak(IRegistry reg) => new(reg,
        new TweakInfo { Id = "two-values", Name = "Dois valores", Description = "d", For = [Focus.Gaming] },
        [
            RegistrySetting.DWord(RegistryHive.CurrentUser, Key, "A", 1),
            RegistrySetting.DWord(RegistryHive.CurrentUser, Key, "B", 1),
        ]);

    [Fact]
    public void KillAfterFirstWrite_NextStartRevertsPartialState()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, Key, "A", 0);

        // Simula o processo sendo finalizado logo depois de gravar o 1º valor (exceção não tratada = nenhum rollback roda).
        reg.FailOnSet = (_, _, name) => name == "B" ? throw new ProcessKilledException() : false;
        var store = BackupStore.Open(fs, clock, TestHelpers.BackupPath);
        var act = () => NewTweak(reg).Apply(store);
        act.Should().Throw<ProcessKilledException>();
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(1, "estado parcial: o 1º valor já foi gravado");

        // Próximo início: novo processo lê o backup do disco.
        reg.FailOnSet = null;
        var restarted = BackupStore.Open(fs, clock, TestHelpers.BackupPath);
        var reconciler = new Reconciler(restarted, id => id == "two-values" ? NewTweak(reg) : null);
        reconciler.HasPending.Should().BeTrue();
        reconciler.Describe().Single().Should().Contain("apply de two-values");

        var outcomes = reconciler.Reconcile();

        outcomes.Single().Result.Status.Should().Be(TweakStatus.Success);
        reg.GetDWord(RegistryHive.CurrentUser, Key, "A").Should().Be(0);
        reg.GetValue(RegistryHive.CurrentUser, Key, "B").Should().BeNull();
        restarted.IncompleteOperations.Should().BeEmpty();
        restarted.HasBackup("two-values").Should().BeFalse();
        BackupStore.Open(fs, clock, TestHelpers.BackupPath).JournalEntries.Should().Contain(j => j.Action == "reconcile");
    }

    [Fact]
    public void KillBeforeAnyWrite_IsSkippedCleanly()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var store = BackupStore.Open(fs, clock, TestHelpers.BackupPath);
        store.BeginOperation("two-values", "apply");

        var restarted = BackupStore.Open(fs, clock, TestHelpers.BackupPath);
        var outcomes = new Reconciler(restarted, _ => NewTweak(new FakeRegistry())).Reconcile();

        outcomes.Single().Result.Status.Should().Be(TweakStatus.Skipped);
        restarted.IncompleteOperations.Should().BeEmpty();
    }

    [Fact]
    public void UnknownTweak_IsReportedForManualRecovery()
    {
        var fs = new FakeFileSystem();
        var store = BackupStore.Open(fs, new FakeClock(), TestHelpers.BackupPath);
        store.BeginOperation("removed-in-new-version", "apply");

        new Reconciler(store, _ => null).Reconcile().Single().Result.Message.Should().Contain("SAFETY.md");
    }
}
