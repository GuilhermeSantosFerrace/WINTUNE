using FluentAssertions;
using WinTune.Core.Games;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class TelemetryTests
{
    private static IReadOnlyList<TelemetrySample> Series(int n, Func<int, SensorReading> f) =>
        Enumerable.Range(0, n).Select(i => new TelemetrySample(DateTimeOffset.UnixEpoch.AddSeconds(i), f(i))).ToList();

    private static readonly BottleneckClassifier Classifier = new();

    [Fact]
    public void GpuSaturated_IsGpuBound()
    {
        var r = Classifier.Classify(Series(60, _ => new SensorReading { GpuUtilizationPercent = 98, CpuTotalPercent = 40, CpuMaxThreadPercent = 70, RamUsedPercent = 50 }));
        r.Primary.Should().Be(Bottleneck.Gpu);
        r.Summary.Should().StartWith("Durante esta sessão: gargalo principal = GPU");
    }

    [Fact]
    public void OneThreadSaturated_WithIdleGpu_IsCpuSingleThread()
    {
        var r = Classifier.Classify(Series(60, _ => new SensorReading { GpuUtilizationPercent = 60, CpuTotalPercent = 30, CpuMaxThreadPercent = 97 }));
        r.Primary.Should().Be(Bottleneck.CpuSingleThread);
    }

    [Fact]
    public void AllCoresSaturated_IsCpuMultiThread()
    {
        var r = Classifier.Classify(Series(60, _ => new SensorReading { GpuUtilizationPercent = 90, CpuTotalPercent = 95, CpuMaxThreadPercent = 99 }));
        r.Primary.Should().Be(Bottleneck.CpuMultiThread);
    }

    [Fact]
    public void HighMemoryWithPaging_IsRamBound_EvenIfGpuBusy()
    {
        var r = Classifier.Classify(Series(60, i => new SensorReading { GpuUtilizationPercent = 97, RamUsedPercent = 94, PagesPerSecond = i % 2 == 0 ? 2500 : 300 }));
        r.Primary.Should().Be(Bottleneck.Ram);
    }

    [Fact]
    public void GpuClockDroppingWhileHot_FlagsThermalThrottle()
    {
        var r = Classifier.Classify(Series(60, i => new SensorReading
        {
            GpuUtilizationPercent = 99,
            GpuClockMhz = i < 20 ? 2700 : i < 40 ? 2500 : 2200,
            GpuTemperatureC = i < 20 ? 70 : 86,
        }));
        r.Findings.Should().ContainSingle(f => f.Id == "thermal-throttle");
        r.Summary.Should().Contain("limitação térmica");
    }

    [Fact]
    public void CpuClockDroppingUnderLoad_FlagsThermalThrottle()
    {
        var r = Classifier.Classify(Series(60, i => new SensorReading
        {
            CpuTotalPercent = 95,
            CpuEffectiveClockMhz = i < 20 ? 4100 : 3200,
        }));
        r.Findings.Should().ContainSingle(f => f.Id == "thermal-throttle");
    }

    [Fact]
    public void StableClocks_NoThermalFinding_AndIdleIsNone()
    {
        var r = Classifier.Classify(Series(60, _ => new SensorReading { GpuUtilizationPercent = 20, CpuTotalPercent = 10, CpuMaxThreadPercent = 30, GpuClockMhz = 2700, GpuTemperatureC = 50 }));
        r.Primary.Should().Be(Bottleneck.None);
        r.Findings.Should().BeEmpty();
    }

    [Fact]
    public void FewSamples_IsInsufficientData()
    {
        Classifier.Classify(Series(5, _ => new SensorReading())).Primary.Should().Be(Bottleneck.InsufficientData);
    }

    [Fact]
    public void MissingSensors_DoNotThrow()
    {
        Classifier.Classify(Series(30, _ => new SensorReading())).Primary.Should().Be(Bottleneck.None);
    }

    [Fact]
    public void CircularBuffer_DropsOldest()
    {
        var b = new CircularBuffer<int>(3);
        foreach (var i in Enumerable.Range(1, 5))
        {
            b.Add(i);
        }

        b.ToList().Should().Equal(3, 4, 5);
        b.Last(2).Should().Equal(4, 5);
    }

    [Fact]
    public void Sampler_RecordsSessionsWithLabel()
    {
        var sensors = new FakeSensors();
        sensors.Readings.Enqueue(new SensorReading { CpuTotalPercent = 10 });
        var clock = new FakeClock();
        var sampler = new Sampler(sensors, clock, capacity: 10);

        sampler.SampleOnce();
        sampler.StartSession("cs2");
        sampler.SampleOnce();
        clock.Advance(TimeSpan.FromSeconds(1));
        sampler.SampleOnce();
        var session = sampler.StopSession();

        session.Label.Should().Be("cs2");
        session.Samples.Should().HaveCount(2).And.OnlyContain(s => s.Label == "cs2");
        sampler.Recent(10).Should().HaveCount(3);
        sampler.IsRecording.Should().BeFalse();
    }

    [Fact]
    public void BackgroundLoad_ExcludesGames_AndFlagsKnownOverlays()
    {
        var reader = new FakeProcessUsageReader();
        reader.Usage.AddRange(
        [
            new ProcessUsage("cs2", 10, 60, 4000, 0),
            new ProcessUsage("Discord", 11, 2, 400, 0),
            new ProcessUsage("chrome#2", 12, 5, 900, 0),
            new ProcessUsage("random", 13, 0.1, 10, 0),
        ]);

        var list = new BackgroundLoadInspector(reader, GameDefinition.Defaults).Inspect();

        list.Should().NotContain(c => c.Usage.Name == "cs2");
        list.Should().Contain(c => c.Usage.Name == "Discord" && c.KnownCategory == "Overlay/comunicação");
        list.Should().Contain(c => c.Usage.Name == "chrome#2" && c.KnownCategory == "Navegador");
    }
}
