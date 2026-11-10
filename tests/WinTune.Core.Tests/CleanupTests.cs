using FluentAssertions;
using WinTune.Core.Cleanup;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class CleanupTests
{
    private const string Temp = @"C:\Users\fulano\AppData\Local\Temp";
    private static readonly DateTime Old = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Recent = new(2026, 11, 10, 11, 0, 0, DateTimeKind.Utc);

    private static (CleanupService Service, FakeFileSystem Fs) World(Action<FakeFileSystem> setup)
    {
        var fs = new FakeFileSystem();
        setup(fs);
        return (new CleanupService(fs, new FakeClock(), new FakeEnvironment()), fs);
    }

    [Fact]
    public void Preview_RespectsAgeLockAndAllowlist_AndDoesNotDelete()
    {
        var (service, fs) = World(fs => fs
            .AddFile($@"{Temp}\old.tmp", length: 100, lastWriteUtc: Old)
            .AddFile($@"{Temp}\new.tmp", length: 50, lastWriteUtc: Recent)
            .AddFile($@"{Temp}\locked.tmp", length: 70, lastWriteUtc: Old, locked: true)
            .AddFile($@"{Temp}\sub\deep.log", length: 30, lastWriteUtc: Old));

        var plan = service.Preview(["user-temp"]);

        plan.Candidates.Select(c => Path.GetFileName(c.Path)).Should().BeEquivalentTo("old.tmp", "deep.log");
        plan.TotalBytes.Should().Be(130);
        plan.Skipped.Should().Contain(s => s.Path.EndsWith("new.tmp", StringComparison.Ordinal) && s.Reason.Contains("24 h"));
        plan.Skipped.Should().Contain(s => s.Path.EndsWith("locked.tmp", StringComparison.Ordinal) && s.Reason == "em uso");
        fs.Deleted.Should().BeEmpty("a prévia nunca apaga");
    }

    [Fact]
    public void SymlinkPointingOutsideAllowlist_IsIgnored()
    {
        var (service, fs) = World(fs => fs
            .AddFile($@"{Temp}\evil.tmp", lastWriteUtc: Old)
            .MapFinalPath($@"{Temp}\evil.tmp", @"C:\Users\fulano\Documents\tese.docx")
            .AddFile($@"{Temp}\link.tmp", lastWriteUtc: Old, reparse: true)
            .AddDirectory($@"{Temp}\junction", reparse: true)
            .AddFile($@"{Temp}\junction\inside.txt", lastWriteUtc: Old));

        var plan = service.Preview(["user-temp"]);

        plan.Candidates.Should().BeEmpty();
        plan.Skipped.Should().Contain(s => s.Path.EndsWith("evil.tmp", StringComparison.Ordinal) && s.Reason.Contains("fora das pastas permitidas"));
        plan.Skipped.Should().Contain(s => s.Path.EndsWith("link.tmp", StringComparison.Ordinal) && s.Reason.Contains("link"));
        service.Execute(plan);
        fs.Deleted.Should().BeEmpty();
    }

    [Fact]
    public void Execute_DeletesOnlyPreviewedFiles_AndReports()
    {
        var (service, fs) = World(fs => fs
            .AddFile($@"{Temp}\a.tmp", length: 10, lastWriteUtc: Old)
            .AddFile($@"{Temp}\b.tmp", length: 20, lastWriteUtc: Old));
        var plan = service.Preview(["user-temp"]);
        fs.AddFile($@"{Temp}\c.tmp", length: 99, lastWriteUtc: Old); // surgiu depois da prévia

        var report = service.Execute(plan);

        report.FreedBytes.Should().Be(30);
        report.DeletedFiles.Should().Be(2);
        fs.FileExists($@"{Temp}\c.tmp").Should().BeTrue();
    }

    [Fact]
    public void Execute_RevalidatesFilesThatBecameLocked()
    {
        var (service, fs) = World(fs => fs.AddFile($@"{Temp}\a.tmp", length: 10, lastWriteUtc: Old));
        var plan = service.Preview(["user-temp"]);
        fs.AddFile($@"{Temp}\a.tmp", length: 10, lastWriteUtc: Old, locked: true);

        var report = service.Execute(plan);

        report.DeletedFiles.Should().Be(0);
        report.Skipped.Should().Contain(s => s.Reason == "em uso");
    }

    [Fact]
    public void Execute_RejectsPlansFromAnotherService()
    {
        var (a, _) = World(_ => { });
        var (b, _) = World(_ => { });
        var act = () => b.Execute(a.Preview(["user-temp"]));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShaderCache_IsOptInWithWarning()
    {
        var cat = CleanupService.DefaultCategories(new FakeEnvironment()).Single(c => c.Id == "shader-cache");
        cat.DefaultSelected.Should().BeFalse();
        cat.Warning.Should().Contain("mais lento");
    }

    [Fact]
    public void UnselectedCategories_AreNotTouched()
    {
        var (service, _) = World(fs => fs
            .AddFile(@"C:\Users\fulano\AppData\Local\D3DSCache\x.bin", lastWriteUtc: Old)
            .AddFile($@"{Temp}\a.tmp", lastWriteUtc: Old));

        service.Preview(["user-temp"]).Candidates.Should().OnlyContain(c => c.CategoryId == "user-temp");
    }
}
