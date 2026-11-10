using FluentAssertions;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Presentation.Tests;

public class TelemetryViewModelTests
{
    private static (TelemetryViewModel Vm, FakeSensors Sensors, FakeClock Clock) Create(Action<FakeSensors>? setup = null)
    {
        var world = new TestWorld();
        var sensors = new FakeSensors();
        setup?.Invoke(sensors);
        var services = new AppServices
        {
            Registry = world.Registry,
            Runner = world.Runner,
            Wmi = world.Wmi,
            FileSystem = world.Fs,
            Environment = new FakeEnvironment(),
            Clock = world.Clock,
            Services = new FakeServiceControl(),
            Processes = new FakeProcessList(),
            Shell = new FakeShellLauncher(),
            Backup = world.Backup,
            Catalog = world.Services.Catalog,
            SensorsFactory = () => sensors,
        };
        return (new TelemetryViewModel(services) { SampleInterval = TimeSpan.FromHours(1) }, sensors, world.Clock);
    }

    [Fact]
    public async Task Session_ClassifiesBottleneck_AndRaisesEvent()
    {
        var (vm, sensors, clock) = Create(s => s.Readings.Enqueue(new SensorReading { GpuUtilizationPercent = 99, CpuTotalPercent = 30 }));
        TelemetrySession? completed = null;
        vm.SessionCompleted += (_, s) => completed = s;
        vm.SessionMinutes = 1;

        await vm.StartSessionCommand.ExecuteAsync(null);
        vm.IsMonitoring.Should().BeTrue();
        vm.IsSessionRecording.Should().BeTrue();
        for (var i = 0; i < 15; i++)
        {
            await vm.SampleNowAsync();
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        await vm.SampleNowAsync();

        vm.IsSessionRecording.Should().BeFalse();
        vm.SessionSummary.Should().Contain("GPU");
        completed.Should().NotBeNull();
        completed!.Label.Should().Be("geral");
        vm.GpuText.Should().Contain("99%");
        await vm.StopMonitoringCommand.ExecuteAsync(null);
        vm.IsMonitoring.Should().BeFalse();
    }

    [Fact]
    public async Task MissingSensors_DegradeWithMessage()
    {
        var (vm, _, _) = Create(s => s.Unavailable.Add("GPU NVIDIA: nvml.dll não encontrada"));

        await vm.StartMonitoringCommand.ExecuteAsync(null);
        await vm.SampleNowAsync();

        vm.HasUnavailable.Should().BeTrue();
        vm.UnavailableText.Should().Contain("nvml.dll");
        vm.GpuText.Should().Contain("—");
        await vm.StopMonitoringCommand.ExecuteAsync(null);
    }
}
