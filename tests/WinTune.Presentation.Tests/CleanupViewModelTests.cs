using FluentAssertions;
using WinTune.Presentation.ViewModels;
using Xunit;

namespace WinTune.Presentation.Tests;

public class CleanupViewModelTests
{
    private const string Temp = @"C:\Users\fulano\AppData\Local\Temp";

    [Fact]
    public async Task CleanRequiresPreview_ConfirmationMentionsIrreversible_AndSelectionChangeInvalidates()
    {
        var world = new TestWorld();
        world.Fs.AddFile($@"{Temp}\a.tmp", length: 2048, lastWriteUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(world.Services, dialogs).Cleanup;

        vm.CleanCommand.CanExecute(null).Should().BeFalse("nenhuma exclusão sem prévia");

        await vm.PreviewCommand.ExecuteAsync(null);
        vm.HasPreview.Should().BeTrue();
        vm.Categories.Single(c => c.Category.Id == "user-temp").PreviewSize.Should().Be("2 KB");
        world.Fs.Deleted.Should().BeEmpty();

        vm.Categories.Single(c => c.Category.Id == "wer-reports").IsSelected = false;
        vm.CleanCommand.CanExecute(null).Should().BeFalse("mudar a seleção invalida a prévia");

        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        dialogs.Confirmations.Should().ContainSingle(c => c.Contains("NÃO pode ser desfeita"));
        world.Fs.Deleted.Should().ContainSingle();
        vm.Summary.Should().Contain("Limpeza concluída");
        vm.CleanCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task DeclinedConfirmation_DeletesNothing()
    {
        var world = new TestWorld();
        world.Fs.AddFile($@"{Temp}\a.tmp", lastWriteUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var vm = new MainViewModel(world.Services, new FakeDialogs { DefaultAnswer = false }).Cleanup;

        await vm.PreviewCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        world.Fs.Deleted.Should().BeEmpty();
    }
}
