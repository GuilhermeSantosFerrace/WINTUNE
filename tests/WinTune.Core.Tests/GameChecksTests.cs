using FluentAssertions;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Core.Games;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class GameChecksTests
{
    private const string Steam = @"C:\Program Files (x86)\Steam";

    private static DiagnosticContext Ctx(FakeWmiReader wmi, FakeRegistry reg, FakeFileSystem fs, SystemProfile? profile = null) =>
        new(wmi, reg, fs, new FakeEnvironment(), new FakeClock(), profile ?? SystemProfile.Unknown);

    private static (FakeRegistry Reg, FakeFileSystem Fs) SteamWithCs2OnD()
    {
        var reg = new FakeRegistry()
            .With(RegistryHive.CurrentUser, GameInstallLocator.SteamKey, "SteamPath", "c:/program files (x86)/steam");
        var fs = new FakeFileSystem()
            .AddFile($@"{Steam}\steamapps\libraryfolders.vdf",
                "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}")
            .AddFile(@"D:\SteamLibrary\steamapps\appmanifest_730.acf", "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}")
            .AddFile(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe");
        return (reg, fs);
    }

    [Fact]
    public void Locator_FindsSteamLibrariesAndUbisoftInstalls()
    {
        var (reg, fs) = SteamWithCs2OnD();
        reg.With(RegistryHive.LocalMachine, $@"{GameInstallLocator.UbisoftInstallsKey}\635", "InstallDir", "E:/Ubisoft/Tom Clancy's Rainbow Six Siege/");
        fs.AddFile(@"E:\Ubisoft\Tom Clancy's Rainbow Six Siege\RainbowSix.exe");
        var locator = new GameInstallLocator(reg, fs);

        locator.SteamLibraries().Should().Contain(@"D:\SteamLibrary");
        locator.Locate(GameDefinition.Defaults.Single(g => g.Target == GameTarget.Cs2))
            .Should().Equal(@"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive");
        locator.Locate(GameDefinition.Defaults.Single(g => g.Target == GameTarget.RainbowSixSiege))
            .Should().Equal(@"E:\Ubisoft\Tom Clancy's Rainbow Six Siege");
    }

    [Fact]
    public void ManifestWithTraversal_IsIgnored()
    {
        GameInstallLocator.ParseInstallDir("\"installdir\"\t\t\"..\\..\\Windows\"").Should().Be(@"..\..\Windows");
        var (reg, fs) = SteamWithCs2OnD();
        fs.AddFile(@"D:\SteamLibrary\steamapps\appmanifest_730.acf", "\"installdir\"\t\t\"..\\..\\Windows\"");

        new GameInstallLocator(reg, fs).Locate(GameDefinition.Defaults.Single(g => g.Target == GameTarget.Cs2)).Should().BeEmpty();
    }

    [Fact]
    public void InstallDrive_WarnsForHdd_InfoForNvme_AndNotFound()
    {
        var (reg, fs) = SteamWithCs2OnD();
        fs.Drives[@"D:\"] = new DriveSpace(1000, 500);
        var cs2 = GameDefinition.Defaults.Where(g => g.Target == GameTarget.Cs2).ToList();
        var hdd = new FakeWmiReader().Add("MSFT_Partition", ("DiskNumber", 1U)).Add("MSFT_PhysicalDisk", ("MediaType", 3), ("BusType", 11));
        var nvme = new FakeWmiReader().Add("MSFT_Partition", ("DiskNumber", 0U)).Add("MSFT_PhysicalDisk", ("MediaType", 4), ("BusType", 17));

        new GameInstallDriveRule(cs2).Evaluate(Ctx(hdd, reg, fs)).Should().ContainSingle(f => f.Severity == Severity.Warning && f.Evidence!.Contains("HDD"));
        new GameInstallDriveRule(cs2).Evaluate(Ctx(nvme, reg, fs)).Should().ContainSingle(f => f.Severity == Severity.Info && f.Evidence!.Contains("NVMe"));
        new GameInstallDriveRule(cs2).Evaluate(Ctx(nvme, new FakeRegistry(), new FakeFileSystem()))
            .Should().ContainSingle(f => f.Title.Contains("não encontrada"));
    }

    [Fact]
    public void InstallDrive_WarnsWhenDiskAlmostFull()
    {
        var (reg, fs) = SteamWithCs2OnD();
        fs.Drives[@"D:\"] = new DriveSpace(1000, 50);
        var ssd = new FakeWmiReader().Add("MSFT_Partition", ("DiskNumber", 0U)).Add("MSFT_PhysicalDisk", ("MediaType", 4), ("BusType", 11));

        new GameInstallDriveRule(GameDefinition.Defaults).Evaluate(Ctx(ssd, reg, fs))
            .Should().Contain(f => f.Title.Contains("quase cheio"));
    }

    [Fact]
    public void RefreshRate_WarnsWhenStuckAt60()
    {
        var at60 = new FakeWmiReader().Add("Win32_VideoController", ("CurrentRefreshRate", 60U), ("MaxRefreshRate", 165U));
        var at165 = new FakeWmiReader().Add("Win32_VideoController", ("CurrentRefreshRate", 165U), ("MaxRefreshRate", 165U));

        new GameRefreshRateRule(GameTarget.Cs2).Evaluate(Ctx(at60, new FakeRegistry(), new FakeFileSystem())).Should().ContainSingle();
        new GameRefreshRateRule(GameTarget.Cs2).Evaluate(Ctx(at165, new FakeRegistry(), new FakeFileSystem())).Should().BeEmpty();
    }

    [Fact]
    public void RamHeadroom_WarnsBelowThreshold()
    {
        var low = new FakeWmiReader().Add("Win32_OperatingSystem", ("FreePhysicalMemory", 3UL * 1024 * 1024));
        var ok = new FakeWmiReader().Add("Win32_OperatingSystem", ("FreePhysicalMemory", 10UL * 1024 * 1024));

        new GameRamHeadroomRule(GameTarget.RainbowSixSiege).Evaluate(Ctx(low, new FakeRegistry(), new FakeFileSystem())).Should().ContainSingle();
        new GameRamHeadroomRule(GameTarget.RainbowSixSiege).Evaluate(Ctx(ok, new FakeRegistry(), new FakeFileSystem())).Should().BeEmpty();
    }

    [Fact]
    public void ShaderCache_ReportsSizes_AndRecommendsNotDeleting()
    {
        var fs = new FakeFileSystem()
            .AddFile(@"C:\Users\fulano\AppData\Local\D3DSCache\a\b.bin", length: 3 * 1024 * 1024)
            .AddFile(@"C:\Users\fulano\AppData\Local\NVIDIA\DXCache\c.bin", length: 1024 * 1024);

        var f = new GameShaderCacheStateRule(GameTarget.Cs2).Evaluate(Ctx(new FakeWmiReader(), new FakeRegistry(), fs)).Single();

        f.Message.Should().Contain("Não apague");
        f.Evidence.Should().Contain("Total 4 MB");
    }

    [Fact]
    public void Rules_DependOnSelectedGames_AndHardware()
    {
        var siegeOnly = GameChecks.Rules(GameDefinition.Defaults, GameTarget.RainbowSixSiege).Select(r => r.Id).ToList();
        siegeOnly.Should().Contain("game-guide-siege").And.NotContain("game-guide-cs2");
        GameChecks.Rules(GameDefinition.Defaults, GameTarget.Cs2).Select(r => r.Id).Should().Contain("game-guide-cs2").And.NotContain("game-guide-siege");

        var oneGpu = new SystemProfile { GpuNames = ["NVIDIA GeForce RTX 4070 SUPER"] };
        var twoGpus = new SystemProfile { GpuNames = ["AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 4070 SUPER"] };
        var ctx1 = Ctx(new FakeWmiReader(), new FakeRegistry(), new FakeFileSystem(), oneGpu);
        var ctx2 = Ctx(new FakeWmiReader(), new FakeRegistry(), new FakeFileSystem(), twoGpus);

        new GameGpuPreferenceGuide(GameTarget.Cs2).Evaluate(ctx1).Should().BeEmpty("com uma GPU não há ganho");
        new GameGpuPreferenceGuide(GameTarget.Cs2).Evaluate(ctx2).Should().ContainSingle();
        new GameNvcpGuide(GameTarget.Cs2).Evaluate(ctx1).Should().ContainSingle();
        new GameNvcpGuide(GameTarget.Cs2).Evaluate(Ctx(new FakeWmiReader(), new FakeRegistry(), new FakeFileSystem())).Should().BeEmpty();
        new GameFsoCompatGuide(GameTarget.Cs2).Evaluate(ctx1).Single().Message.Should().Contain("Meça");
    }

    [Fact]
    public void Guides_NeverMentionVulkanForSiege_NorInventFlags()
    {
        var ctx = Ctx(new FakeWmiReader(), new FakeRegistry(), new FakeFileSystem());
        new GameGuideSiege().Evaluate(ctx).Single().Message.Should().NotContainEquivalentOf("vulkan");
        new GameGuideCs2().Evaluate(ctx).Single().Message.Should().Contain("documentados pela Valve").And.NotContain("-novid");
    }

    [Fact]
    public void FullGameAnalyzer_RunsWithoutErrors()
    {
        var (reg, fs) = SteamWithCs2OnD();
        var findings = new Analyzer(GameChecks.Rules(GameDefinition.Defaults, GameTarget.RainbowSixSiege | GameTarget.Cs2))
            .Run(Ctx(new FakeWmiReader(), reg, fs));

        findings.Should().NotContain(f => f.Severity == Severity.Error);
        findings.Should().OnlyContain(f => f.Category == FindingCategory.Game);
    }
}
