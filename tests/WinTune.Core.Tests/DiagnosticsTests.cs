using FluentAssertions;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class DiagnosticsTests
{
    private const ulong Gb8 = 8UL * 1024 * 1024 * 1024;

    private static DiagnosticContext Ctx(FakeWmiReader? wmi = null, FakeRegistry? reg = null, FakeFileSystem? fs = null,
        SystemProfile? profile = null, FakeClock? clock = null) =>
        new(wmi ?? new FakeWmiReader(), reg ?? new FakeRegistry(), fs ?? new FakeFileSystem(), new FakeEnvironment(),
            clock ?? new FakeClock(), profile ?? SystemProfile.Unknown);

    private static IReadOnlyList<Finding> Eval(IDiagnosticRule rule, DiagnosticContext ctx) => rule.Evaluate(ctx).ToList();

    [Fact]
    public void RamBelowRated_PositiveAndNegative()
    {
        var slow = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Speed", 3200U), ("ConfiguredClockSpeed", 2133U), ("Capacity", Gb8));
        var ok = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Speed", 3200U), ("ConfiguredClockSpeed", 3200U), ("Capacity", Gb8));

        Eval(new RamBelowRatedRule(), Ctx(slow)).Should().ContainSingle(f => f.Severity == Severity.Critical && f.Evidence!.Contains("2133"));
        Eval(new RamBelowRatedRule(), Ctx(ok)).Should().BeEmpty();
    }

    [Fact]
    public void RamSingleChannel_PositiveAndNegative()
    {
        var one = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", Gb8));
        var two = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", Gb8)).Add("Win32_PhysicalMemory", ("Capacity", Gb8));

        Eval(new RamSingleChannelRule(), Ctx(one)).Should().ContainSingle();
        Eval(new RamSingleChannelRule(), Ctx(two)).Should().BeEmpty();
    }

    [Fact]
    public void RamCapacityLow_At16GbYes_At32GbNo()
    {
        var sixteen = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", Gb8)).Add("Win32_PhysicalMemory", ("Capacity", Gb8));
        var thirtyTwo = new FakeWmiReader().Add("Win32_PhysicalMemory", ("Capacity", 2 * Gb8)).Add("Win32_PhysicalMemory", ("Capacity", 2 * Gb8));

        Eval(new RamCapacityLowRule(), Ctx(sixteen)).Should().ContainSingle(f => f.Evidence!.Contains("16 GB"));
        Eval(new RamCapacityLowRule(), Ctx(thirtyTwo)).Should().BeEmpty();
    }

    [Fact]
    public void RebarUnknown_AlwaysInformational()
    {
        Eval(new RebarUnknownRule(), Ctx()).Should().ContainSingle(f => f.Severity == Severity.Info);
    }

    [Fact]
    public void VbsActive_OnlyWhenRunning()
    {
        var running = new FakeWmiReader().Add("Win32_DeviceGuard", ("VirtualizationBasedSecurityStatus", 2U));
        var off = new FakeWmiReader().Add("Win32_DeviceGuard", ("VirtualizationBasedSecurityStatus", 0U));

        Eval(new VbsActiveRule(), Ctx(running)).Should().ContainSingle(f => f.Message.Contains("não o desativa"));
        Eval(new VbsActiveRule(), Ctx(off)).Should().BeEmpty();
    }

    [Fact]
    public void ChipsetDriverMissing_OnlyForAmdWithoutAmdSystemDrivers()
    {
        var amdProfile = new SystemProfile { CpuVendor = "AuthenticAMD" };
        var missing = new FakeWmiReader().Add("Win32_PnPSignedDriver", ("DeviceName", "PCI"), ("DriverProviderName", "Microsoft"));
        var present = new FakeWmiReader().Add("Win32_PnPSignedDriver", ("DeviceName", "AMD GPIO Controller"), ("DriverProviderName", "Advanced Micro Devices, Inc"));

        Eval(new ChipsetDriverMissingRule(), Ctx(missing, profile: amdProfile)).Should().ContainSingle();
        Eval(new ChipsetDriverMissingRule(), Ctx(present, profile: amdProfile)).Should().BeEmpty();
        Eval(new ChipsetDriverMissingRule(), Ctx(missing, profile: new SystemProfile { CpuVendor = "GenuineIntel" })).Should().BeEmpty();
    }

    [Fact]
    public void FastStartup_PositiveAndNegative()
    {
        var on = new FakeRegistry().With(RegistryHive.LocalMachine, FastStartupRule.Key, "HiberbootEnabled", 1);
        var off = new FakeRegistry().With(RegistryHive.LocalMachine, FastStartupRule.Key, "HiberbootEnabled", 0);

        Eval(new FastStartupRule(), Ctx(reg: on)).Should().ContainSingle();
        Eval(new FastStartupRule(), Ctx(reg: off)).Should().BeEmpty();
    }

    [Fact]
    public void DiskLowSpace_Below15Percent()
    {
        var low = new FakeFileSystem();
        low.Drives[@"C:\"] = new DriveSpace(1000, 100);
        var ok = new FakeFileSystem();
        ok.Drives[@"C:\"] = new DriveSpace(1000, 500);

        Eval(new DiskLowSpaceRule(), Ctx(fs: low)).Should().ContainSingle();
        Eval(new DiskLowSpaceRule(), Ctx(fs: ok)).Should().BeEmpty();
    }

    [Fact]
    public void VCacheNote_For5700X3D_NotForDualCcd()
    {
        Eval(new VCacheNoteRule(), Ctx(profile: new SystemProfile { CpuName = "AMD Ryzen 7 5700X3D 8-Core Processor" }))
            .Should().ContainSingle(f => f.Message.Contains("não precisa"));
        Eval(new VCacheNoteRule(), Ctx(profile: new SystemProfile { CpuName = "AMD Ryzen 9 7950X3D 16-Core Processor" })).Should().BeEmpty();
    }

    [Fact]
    public void GpuDriverOld_OnlyNvidiaOlderThan180Days()
    {
        var old = new FakeWmiReader().Add("Win32_VideoController", ("Name", "NVIDIA GeForce RTX 4070 SUPER"),
            ("DriverVersion", "32.0.15.6094"), ("DriverDate", "20240101000000.000000-000"));
        var recent = new FakeWmiReader().Add("Win32_VideoController", ("Name", "NVIDIA GeForce RTX 4070 SUPER"),
            ("DriverVersion", "32.0.15.9999"), ("DriverDate", "20261001000000.000000-000"));

        Eval(new GpuDriverOldRule(), Ctx(old)).Should().ContainSingle(f => f.Evidence!.Contains("560.94"));
        Eval(new GpuDriverOldRule(), Ctx(recent)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("bogus", null)]
    public void NvidiaVersion_IsDerivedFromWindowsVersion(string input, string? expected)
    {
        GpuDriverOldRule.NvidiaVersion(input).Should().Be(expected);
    }

    [Fact]
    public void Analyzer_IsolatesFailingRules()
    {
        var wmi = new FakeWmiReader().Fail("Win32_PhysicalMemory");
        var reg = new FakeRegistry().With(RegistryHive.LocalMachine, FastStartupRule.Key, "HiberbootEnabled", 1);

        var findings = new Analyzer(Analyzer.StaticRules()).Run(Ctx(wmi, reg));

        findings.Should().Contain(f => f.Id == "ram-below-rated" && f.Severity == Severity.Error);
        findings.Should().Contain(f => f.Id == "fast-startup" && f.Severity == Severity.Info);
        findings.Should().Contain(f => f.Id == "rebar-unknown");
    }

    [Fact]
    public void Analyzer_NeverWrites()
    {
        var reg = new FakeRegistry().With(RegistryHive.LocalMachine, FastStartupRule.Key, "HiberbootEnabled", 1);
        var writes = reg.WriteCount;

        new Analyzer(Analyzer.StaticRules()).Run(Ctx(reg: reg));

        reg.WriteCount.Should().Be(writes);
    }

    [Fact]
    public void ProfileDetector_ToleratesFailures()
    {
        var wmi = new FakeWmiReader().Fail("Win32_Processor")
            .Add("Win32_VideoController", ("Name", "NVIDIA GeForce RTX 4070 SUPER"))
            .Add("Win32_VideoController", ("Name", "Microsoft Basic Display Adapter"));

        var profile = SystemProfileDetector.Detect(wmi, new FakeProcessRunner(), new FakeEnvironment());

        profile.CpuName.Should().BeEmpty();
        profile.GpuNames.Should().Equal("NVIDIA GeForce RTX 4070 SUPER");
        profile.HasNvidiaGpu.Should().BeTrue();
    }
}
