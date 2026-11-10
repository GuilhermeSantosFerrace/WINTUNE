using FluentAssertions;
using WinTune.Presentation.ViewModels;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Presentation.Tests;

public class HistoryViewModelTests
{
    [Fact]
    public async Task ExportWritesLocalFile_AndHistoryShowsJournal_AndLogsOpen()
    {
        var world = new TestWorld();
        world.Backup.Journal("apply", "game-mode", "success");
        var dialogs = new FakeDialogs { SaveFile = @"C:\Users\fulano\Documents\relatorio.md" };
        var main = new MainViewModel(world.Services, dialogs);
        var vm = main.History;

        vm.RefreshCommand.Execute(null);
        vm.Entries.Should().Contain(e => e.Item == "game-mode" && e.Result == "success");

        await vm.ExportMarkdownCommand.ExecuteAsync(null);
        world.Fs.Content(@"C:\Users\fulano\Documents\relatorio.md").Should().StartWith("# Relatório do WinTune").And.NotContain("fulano");

        vm.OpenLogsCommand.Execute(null);
        ((FakeShellLauncher)world.Services.Shell).Opened.Should().ContainSingle(p => p.EndsWith(@"WinTune\logs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledSaveDialog_WritesNothing()
    {
        var world = new TestWorld();
        var vm = new MainViewModel(world.Services, new FakeDialogs()).History;
        await vm.ExportJsonCommand.ExecuteAsync(null);
        world.Fs.FileExists(@"C:\Users\fulano\Documents\relatorio.json").Should().BeFalse();
    }
}
