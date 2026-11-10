using System.Globalization;
using System.Text;
using FluentAssertions;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;
using WinTune.Presentation.ViewModels;
using Xunit;

namespace WinTune.Presentation.Tests;

public class BenchmarkViewModelTests
{
    private static TelemetrySession Session(string label, int minute) =>
        new(label, DateTimeOffset.UnixEpoch.AddMinutes(minute), DateTimeOffset.UnixEpoch.AddMinutes(minute + 2),
            Enumerable.Range(0, 30).Select(i => new TelemetrySample(DateTimeOffset.UnixEpoch.AddMinutes(minute).AddSeconds(i),
                new SensorReading { GpuUtilizationPercent = 95, GpuTemperatureC = 70, RamUsedPercent = 60, PagesPerSecond = 10 }, label)).ToList());

    private static string Csv(double frameTimeMs)
    {
        var sb = new StringBuilder("Application,ProcessID,MsBetweenPresents\n");
        for (var i = 0; i < 500; i++)
        {
            sb.Append("cs2.exe,1,").Append(frameTimeMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    [Fact]
    public async Task TelemetrySessions_BecomeRuns_AndCompareReportsImprovement()
    {
        var world = new TestWorld();
        var dialogs = new FakeDialogs();
        var main = new MainViewModel(world.Services, dialogs);
        var vm = main.Benchmark;
        vm.SaveRunCommand.CanExecute(null).Should().BeFalse();

        var minute = 0;
        foreach (var (condition, ft) in new[] { ("antes", 5.0), ("antes", 5.0), ("antes", 5.02), ("antes", 4.98), ("depois", 4.0), ("depois", 4.01), ("depois", 3.99), ("depois", 4.0) })
        {
            var csv = $@"C:\Users\fulano\Documents\pm-{minute}.csv";
            world.Fs.AddFile(csv, Csv(ft));
            vm.SetPendingSession(Session("cs2", minute += 5));
            vm.Condition = condition;
            dialogs.OpenFile = csv;
            vm.PickCsvCommand.Execute(null);
            await vm.SaveRunCommand.ExecuteAsync(null);
        }

        vm.Runs.Should().HaveCount(8);
        vm.SelectedGame = "cs2";
        vm.CompareCommand.Execute(null);

        vm.Verdict.Should().Be("cs2: melhora");
        vm.Results.Should().Contain(r => r.StartsWith("FPS médio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutCsv_ShowsTelemetryOnly()
    {
        var world = new TestWorld();
        var vm = new MainViewModel(world.Services, new FakeDialogs()).Benchmark;
        for (var i = 0; i < 6; i++)
        {
            vm.SetPendingSession(Session("siege", i * 5));
            vm.Condition = i < 3 ? "antes" : "depois";
            await vm.SaveRunCommand.ExecuteAsync(null);
        }

        vm.CompareCommand.Execute(null);

        vm.SelectedGame.Should().Be("siege");
        vm.Results.Where(r => !r.StartsWith("Obs.:", StringComparison.Ordinal)).Should().NotContain(r => r.Contains("FPS"));
        vm.Results.Should().Contain(r => r.Contains("Nenhum FPS é estimado"));
    }
}
