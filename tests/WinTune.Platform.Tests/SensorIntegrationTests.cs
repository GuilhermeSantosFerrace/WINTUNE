using System.Diagnostics;
using FluentAssertions;
using WinTune.Core.Telemetry;
using WinTune.Platform.Sensors;
using Xunit;
using Xunit.Abstractions;

namespace WinTune.Platform.Tests;

/// <summary>Coleção sem paralelismo: a medição de overhead usa a CPU do processo inteiro.</summary>
[CollectionDefinition("Overhead", DisableParallelization = true)]
public sealed class OverheadCollection;

/// <summary>Sensores reais (somente leitura) e medição de overhead. Fora do CI.</summary>
[Trait("Category", "Integration")]
[Collection("Overhead")]
public class SensorIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public void UserModeSensors_ReadCpuAndRam()
    {
        using var sensors = new UserModeSensors(new WmiReader());
        sensors.Read();
        Thread.Sleep(1000);
        var r = sensors.Read();

        output.WriteLine($"Indisponíveis: {string.Join(" | ", sensors.UnavailableSources)}");
        output.WriteLine($"CPU {r.CpuTotalPercent:0.0}% max thread {r.CpuMaxThreadPercent:0.0}% clock {r.CpuEffectiveClockMhz} MHz temp {r.CpuTemperatureC}");
        output.WriteLine($"GPU {r.GpuUtilizationPercent:0.0}% {r.GpuClockMhz} MHz {r.GpuTemperatureC} °C VRAM {r.GpuMemoryUsedMb}/{r.GpuMemoryTotalMb}");
        output.WriteLine($"RAM {r.RamUsedPercent:0.0}% livre {r.RamAvailableMb} MB pag {r.PagesPerSecond:0} disco {r.DiskBusyPercent:0.0}%");

        r.CpuTotalPercent.Should().NotBeNull();
        r.RamUsedPercent.Should().NotBeNull();
    }

    [Fact]
    public void Sampling_At1Hz_UsesLessThanOnePercentCpu()
    {
        using var sensors = new UserModeSensors(new WmiReader());
        var sampler = new Sampler(sensors, new SystemClock());
        sampler.SampleOnce();
        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();

        for (var i = 0; i < 20; i++)
        {
            sampler.SampleOnce();
            Thread.Sleep(1000);
        }

        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        var percentOfMachine = cpu / (wall.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        var percentOfOneCore = cpu / wall.Elapsed.TotalMilliseconds * 100;
        output.WriteLine($"Overhead: {percentOfMachine:0.000}% da máquina ({percentOfOneCore:0.00}% de um núcleo) em {wall.Elapsed.TotalSeconds:0} s");

        percentOfMachine.Should().BeLessThan(1.0);
    }

    [Fact]
    public void ProcessUsage_IsReadable()
    {
        new ProcessUsageReader(new WmiReader()).Read().Should().NotBeEmpty();
    }
}
