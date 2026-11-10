using System.Text.Json;
using FluentAssertions;
using WinTune.Core.Benchmark;
using WinTune.Core.Domain;
using WinTune.Core.Reports;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class ReportTests
{
    private static (ReportBuilder Builder, WinTuneReport Report) Build()
    {
        var env = new FakeEnvironment { UserName = "fulano", MachineName = "PC-DO-FULANO" };
        var wmi = new FakeWmiReader()
            .Add("Win32_PhysicalMemory", ("Capacity", 8UL * 1024 * 1024 * 1024), ("Speed", 3200U), ("ConfiguredClockSpeed", 2133U), ("SerialNumber", "SN-123456"))
            .Add("Win32_OperatingSystem", ("Caption", "Microsoft Windows 11 Pro"));
        var (store, _, _) = TestHelpers.NewStore();
        var reg = new FakeRegistry();
        var catalog = new TweakCatalog(reg, new FakeProcessRunner());
        catalog.Find("gamedvr-off")!.Apply(store);
        store.Journal("cleanup", "-", "success", @"apagado C:\Users\fulano\AppData\Local\Temp\x.tmp em PC-DO-FULANO");

        var builder = new ReportBuilder(wmi, env, new FakeClock());
        var report = builder.Build("1.0.0",
            new SystemProfile { CpuName = "AMD Ryzen 7 5700X3D 8-Core Processor", GpuNames = ["NVIDIA GeForce RTX 4070 SUPER"], TotalRamBytes = 16UL * 1024 * 1024 * 1024 },
            [new Finding("ram-below-rated", "Memória abaixo da velocidade nominal", "Ative EXPO/XMP", Severity.Critical, @"C:\Users\fulano\x")],
            store, catalog.Find,
            [new BenchmarkRun { Game = "cs2", Condition = "antes", Telemetry = new TelemetrySummary { SampleCount = 10 } }]);
        return (builder, report);
    }

    [Fact]
    public void Exports_ContainNoUserNameMachineNameOrSerial()
    {
        var (builder, report) = Build();

        foreach (var text in new[] { builder.ToMarkdown(report), builder.ToJson(report) })
        {
            text.Should().NotContainEquivalentOf("fulano");
            text.Should().NotContainEquivalentOf("PC-DO-FULANO");
            text.Should().NotContain("SN-123456");
            text.Should().Contain("5700X3D").And.Contain("RTX 4070 SUPER");
        }

        builder.ToMarkdown(report).Should().Contain("gamedvr-off").And.Contain("2133 MT/s").And.Contain("%USERPROFILE%");
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var (builder, report) = Build();
        var json = builder.ToJson(report);

        var back = JsonSerializer.Deserialize<WinTuneReport>(json, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;

        back.AppVersion.Should().Be("1.0.0");
        back.Hardware.MemoryModules.Should().ContainSingle(m => m.ConfiguredMts == 2133);
        back.AppliedTweaks.Should().ContainSingle(t => t.Id == "gamedvr-off");
        back.Findings.Single().Severity.Should().Be(Severity.Critical);
        back.BenchmarkRuns.Should().ContainSingle();
    }
}
