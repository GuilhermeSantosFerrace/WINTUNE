using System.Globalization;
using System.Text;
using FluentAssertions;
using WinTune.Core.Benchmark;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class BenchmarkTests
{
    private static BenchmarkRun Run(string game, string condition, int index, double? fps, double gpuTemp = 70, double jitter = 0)
    {
        FrameStats? frames = fps is null
            ? null
            : FrameStats.From(Enumerable.Range(0, 1000).Select(i => (1000.0 / (fps.Value + jitter)) + (i % 10 == 0 ? 2 : 0)).ToList(), "PresentMon");
        return new BenchmarkRun
        {
            Game = game,
            Condition = condition,
            StartedUtc = DateTimeOffset.UnixEpoch.AddMinutes(index),
            Telemetry = new TelemetrySummary { SampleCount = 60, GpuMaxTemperatureC = gpuTemp, RamMaxUsedPercent = 70, GpuAveragePercent = 90, PagesPerSecondAverage = 50 },
            Frames = frames,
        };
    }

    private static List<BenchmarkRun> Runs(string condition, params double[] fps) =>
        fps.Select((f, i) => Run("cs2", condition, (condition == "antes" ? 0 : 100) + i, f, jitter: 0)).ToList();

    private static readonly Comparator Comparator = new();

    [Fact]
    public void ClearGain_IsImprovement()
    {
        var r = Comparator.Compare(Runs("antes", 150, 200, 201, 199), Runs("depois", 160, 230, 231, 229));
        r.Overall.Should().Be(Verdict.Improvement);
        r.Metrics.Single(m => m.Metric == "FPS médio").ChangePercent.Should().BeApproximately(15, 1);
    }

    [Fact]
    public void ClearLoss_IsRegression()
    {
        Comparator.Compare(Runs("antes", 150, 230, 231, 229), Runs("depois", 150, 200, 201, 199)).Overall.Should().Be(Verdict.Regression);
    }

    [Fact]
    public void DifferenceWithinNoise_IsNoSignificantDifference()
    {
        // Média depois 2% maior, mas rodadas variam muito.
        Comparator.Compare(Runs("antes", 100, 180, 220, 200), Runs("depois", 100, 170, 240, 202)).Overall
            .Should().Be(Verdict.NoSignificantDifference);
    }

    [Fact]
    public void FirstRunIsDiscarded()
    {
        // A 1ª rodada "depois" é muito pior (shader), mas é descartada.
        var r = Comparator.Compare(Runs("antes", 200, 200, 201, 199), Runs("depois", 50, 200, 200, 201));
        r.Overall.Should().Be(Verdict.NoSignificantDifference);
        r.Notes.Should().Contain(n => n.Contains("descartada"));
    }

    [Fact]
    public void FewerThanThreeRuns_IsInsufficient()
    {
        Comparator.Compare(Runs("antes", 200, 200), Runs("depois", 220, 220, 220)).Overall.Should().Be(Verdict.InsufficientData);
    }

    [Fact]
    public void DifferentGames_AreNeverMixed()
    {
        var before = Runs("antes", 200, 200, 200);
        var after = Runs("depois", 220, 220, 220).Select(r => r with { Game = "siege" }).ToList();
        var report = Comparator.Compare(before, after);
        report.Overall.Should().Be(Verdict.InsufficientData);
        report.Notes.Single().Should().Contain("jogos diferentes");
    }

    [Fact]
    public void WithoutFrameData_OnlyTelemetry_NoFps()
    {
        var before = Enumerable.Range(0, 3).Select(i => Run("siege", "antes", i, null, gpuTemp: 80)).ToList();
        var after = Enumerable.Range(0, 3).Select(i => Run("siege", "depois", 10 + i, null, gpuTemp: 70)).ToList();

        var report = Comparator.Compare(before, after);

        report.Metrics.Should().NotContain(m => m.Metric.Contains("FPS"));
        report.Notes.Should().Contain(n => n.Contains("Nenhum FPS é estimado"));
        report.Overall.Should().Be(Verdict.InsufficientData);
    }

    [Fact]
    public void FrameStats_ComputesAverageAndOnePercentLow()
    {
        var ft = Enumerable.Repeat(5.0, 990).Concat(Enumerable.Repeat(20.0, 10)).ToList();
        var s = FrameStats.From(ft, "PresentMon")!;
        s.AverageFps.Should().BeApproximately(1000 / 5.15, 0.1);
        s.OnePercentLowFps.Should().BeApproximately(1000 / 5.0, 0.1);
        FrameStats.From([5, 5, 5], "x").Should().BeNull("poucos quadros");
    }

    [Fact]
    public void PresentMon_ParsesV1AndV2_AndFiltersProcess()
    {
        var v1 = new StringBuilder("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,MsBetweenPresents\n");
        v1.Append("cs2.exe,1,0x1,DXGI,0,0,0,0.1,6.5\n").Append("other.exe,2,0x1,DXGI,0,0,0,0.1,16.6\n").Append("cs2.exe,1,0x1,DXGI,0,0,0,0.1,7.0\r\n");
        PresentMonCsv.Parse(v1.ToString(), "cs2.exe").Should().Equal(6.5, 7.0);

        var v2 = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,FrameTime,CPUBusy\n" +
                 "RainbowSix.exe,9,0x1,DXGI,0,0,1,Hardware: Independent Flip,Application,1.0,4.25,3.1\n";
        PresentMonCsv.Parse(v2).Should().Equal(4.25);

        var act = () => PresentMonCsv.Parse("a,b,c\n1,2,3");
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Store_RoundTrips_AndSkipsInvalidFiles()
    {
        var fs = new FakeFileSystem();
        var store = new BenchmarkStore(fs, new FakeEnvironment());
        var run = Run("cs2", "antes", 0, 200);
        store.Save(run);
        fs.AddFile(Path.Combine(store.Directory, "lixo.json"), "{nao json");

        var loaded = store.LoadAll(out var errors);

        loaded.Should().ContainSingle(r => r.Id == run.Id && r.Frames!.AverageFps > 0);
        errors.Should().ContainSingle(e => e.StartsWith("lixo.json", StringComparison.Ordinal));
        store.Directory.Should().EndWith(@"WinTune\sessions");
    }

    [Fact]
    public void TelemetrySummary_FromSamples()
    {
        var samples = new[]
        {
            new TelemetrySample(DateTimeOffset.UnixEpoch, new SensorReading { CpuTotalPercent = 10, RamUsedPercent = 50, GpuTemperatureC = 60 }),
            new TelemetrySample(DateTimeOffset.UnixEpoch, new SensorReading { CpuTotalPercent = 30, RamUsedPercent = 70 }),
        };
        var s = TelemetrySummary.From(samples);
        s.CpuAveragePercent.Should().Be(20);
        s.RamMaxUsedPercent.Should().Be(70);
        s.GpuMaxTemperatureC.Should().Be(60);
        s.GpuAveragePercent.Should().BeNull();
        _ = CultureInfo.InvariantCulture;
    }
}
