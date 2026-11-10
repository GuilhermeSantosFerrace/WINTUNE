using FluentAssertions;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Platform.Tests;

public class FakeRegistryTests
{
    [Fact]
    public void SetThenGet_ReturnsValue_CaseInsensitivePath()
    {
        var reg = new FakeRegistry();
        reg.SetDWord(RegistryHive.CurrentUser, @"Software\A\B", "X", 5);

        reg.GetDWord(RegistryHive.CurrentUser, @"software\a\b", "x").Should().Be(5);
        reg.KeyExists(RegistryHive.CurrentUser, @"Software\A").Should().BeTrue();
        reg.GetValue(RegistryHive.LocalMachine, @"Software\A\B", "X").Should().BeNull();
    }

    [Fact]
    public void Delete_RemovesValue_AndIsNoOpWhenMissing()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, "K", "V", "texto");
        reg.DeleteValue(RegistryHive.CurrentUser, "K", "V");
        reg.DeleteValue(RegistryHive.CurrentUser, "K", "V");
        reg.DeleteValue(RegistryHive.CurrentUser, "Missing", "V");

        reg.GetValue(RegistryHive.CurrentUser, "K", "V").Should().BeNull();
    }

    [Fact]
    public void FailOnSet_Throws_AndDoesNotWrite()
    {
        var reg = new FakeRegistry { FailOnSet = (_, _, name) => name == "B" };
        reg.SetDWord(RegistryHive.CurrentUser, "K", "A", 1);

        var act = () => reg.SetDWord(RegistryHive.CurrentUser, "K", "B", 1);

        act.Should().Throw<UnauthorizedAccessException>();
        reg.GetValue(RegistryHive.CurrentUser, "K", "B").Should().BeNull();
        reg.WriteCount.Should().Be(1);
    }

    [Fact]
    public void SubKeysAndValueNames_AreListed()
    {
        var reg = new FakeRegistry()
            .With(RegistryHive.LocalMachine, @"Root\One", "a", 1)
            .With(RegistryHive.LocalMachine, @"Root\Two\Deep", "b", 2);

        reg.GetSubKeyNames(RegistryHive.LocalMachine, "Root").Should().BeEquivalentTo("one", "two");
        reg.GetValueNames(RegistryHive.LocalMachine, @"Root\One").Should().Equal("a");
    }
}

public class RegistryValueTests
{
    [Fact]
    public void Equality_IsByContent_IncludingBinary()
    {
        RegistryValue.FromBinary([1, 2, 3]).Should().Be(RegistryValue.FromBinary([1, 2, 3]));
        RegistryValue.FromBinary([1, 2, 3]).Should().NotBe(RegistryValue.FromBinary([1, 2, 4]));
        RegistryValue.FromDWord(1).Should().NotBe(RegistryValue.FromString("1"));
        RegistryValue.FromString("a").Should().NotBe(RegistryValue.FromExpandString("a"));
    }

    [Fact]
    public void Binary_IsDefensivelyCopied()
    {
        var data = new byte[] { 2, 0 };
        var value = RegistryValue.FromBinary(data);
        data[0] = 3;
        value.Binary![0].Should().Be(2);
    }
}

public class FakeProcessRunnerTests
{
    [Fact]
    public void ReturnsRegisteredResponse_AndRecordsCalls()
    {
        var runner = new FakeProcessRunner().On("powercfg.exe", ["/getactivescheme"], "GUID: x");

        var result = runner.Run("powercfg.exe", ["/getactivescheme"], TimeSpan.FromSeconds(5));

        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().Be("GUID: x");
        runner.Calls.Should().ContainSingle();
    }

    [Fact]
    public void UnknownCommand_Fails()
    {
        var runner = new FakeProcessRunner();
        runner.Run("x.exe", [], TimeSpan.FromSeconds(1)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ZeroTimeout_IsRejected()
    {
        var runner = new FakeProcessRunner();
        var act = () => runner.Run("x.exe", [], TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class FakeWmiAndFileSystemTests
{
    [Fact]
    public void Wmi_ReturnsRowsByClass()
    {
        var wmi = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", 8589934592UL), ("Speed", 3200U));

        var rows = wmi.Query(WmiScopes.Cimv2, "SELECT Capacity, Speed FROM Win32_PhysicalMemory");

        rows.Should().ContainSingle();
        rows[0].GetUInt64("capacity").Should().Be(8589934592UL);
        rows[0].GetUInt64("Speed").Should().Be(3200UL);
        rows[0].GetString("Missing").Should().BeNull();
    }

    [Fact]
    public void FileSystem_RecursiveEnumeration_SkipsReparseDirectories()
    {
        var fs = new FakeFileSystem()
            .AddFile(@"C:\T\a.txt")
            .AddFile(@"C:\T\sub\b.txt")
            .AddDirectory(@"C:\T\link", reparse: true)
            .AddFile(@"C:\T\link\c.txt");

        fs.EnumerateFiles(@"C:\T", "*", recursive: true).Select(f => Path.GetFileName(f.FullPath))
            .Should().BeEquivalentTo("a.txt", "b.txt");
    }

    [Fact]
    public void FileSystem_LockedFile_CannotBeDeleted()
    {
        var fs = new FakeFileSystem().AddFile(@"C:\T\a.txt", locked: true);
        fs.IsFileLocked(@"C:\T\a.txt").Should().BeTrue();
        var act = () => fs.DeleteFile(@"C:\T\a.txt");
        act.Should().Throw<IOException>();
    }
}

public class ProcessRunnerTests
{
    [Theory]
    [InlineData("powercfg.exe", @"C:\Windows\System32\powercfg.exe")]
    [InlineData(@"WindowsPowerShell\v1.0\powershell.exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    public void ResolveExecutable_RelativeNames_StayInSystem32(string input, string expected)
    {
        ProcessRunner.ResolveExecutable(input, @"C:\Windows\System32").Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(@"..\evil.exe")]
    [InlineData("script.bat")]
    [InlineData("")]
    public void ResolveExecutable_RejectsUnsafeInput(string input)
    {
        var act = () => ProcessRunner.ResolveExecutable(input, @"C:\Windows\System32");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Run_RejectsMissingTimeout()
    {
        var act = () => new ProcessRunner().Run("hostname.exe", [], TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
