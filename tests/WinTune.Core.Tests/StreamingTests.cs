using FluentAssertions;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Core.Games;
using WinTune.Core.Recommendations;
using WinTune.Core.Telemetry;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class StreamingTests
{
    private static DiagnosticContext Ctx(FakeWmiReader? wmi = null, FakeFileSystem? fs = null, SystemProfile? profile = null) =>
        new(wmi ?? new FakeWmiReader(), new FakeRegistry(), fs ?? new FakeFileSystem(), new FakeEnvironment(), new FakeClock(), profile ?? SystemProfile.Unknown);

    private static FakeWmiReader Adapters(params (string Name, uint Status, ulong Speed)[] items)
    {
        var wmi = new FakeWmiReader();
        foreach (var (name, status, speed) in items)
        {
            wmi.Add("Win32_NetworkAdapter", ("Name", name), ("NetConnectionStatus", status), ("Speed", speed), ("PhysicalAdapter", true));
        }

        return wmi;
    }

    [Fact]
    public void Network_WarnsWhenOnlyWifiIsConnected()
    {
        var wmi = Adapters(("Intel(R) Wi-Fi 6E AX211 160MHz", 2, 1_200_000_000), ("Realtek PCIe GbE Family Controller", 7, 0));

        var f = new StreamingNetworkRule().Evaluate(Ctx(wmi)).Single();

        f.Severity.Should().Be(Severity.Warning);
        f.Title.Should().Contain("Wi-Fi");
        f.Category.Should().Be(FindingCategory.Streaming);
        f.Message.Should().Contain("não mede");
    }

    [Fact]
    public void Network_InfoForGigabitCable_AndSlowCableWarnsAsInfo()
    {
        var gig = new StreamingNetworkRule().Evaluate(Ctx(Adapters(("Realtek PCIe GbE Family Controller", 2, 1_000_000_000)))).Single();
        var slow = new StreamingNetworkRule().Evaluate(Ctx(Adapters(("Realtek PCIe FE Family Controller", 2, 100_000_000)))).Single();

        gig.Title.Should().Be("Conexão cabeada");
        slow.Title.Should().Contain("abaixo de 1 Gb/s");
        slow.Evidence.Should().Contain("100 Mb/s");
    }

    [Fact]
    public void Network_IgnoresVirtualAdaptersAndDisconnected_AndNothingConnectedIsSilent()
    {
        var wmi = Adapters(("Hyper-V Virtual Ethernet Adapter", 2, 10_000_000_000), ("Bluetooth Device (Personal Area Network)", 2, 3_000_000));
        new StreamingNetworkRule().Evaluate(Ctx(wmi)).Should().BeEmpty();
        new StreamingNetworkRule().Evaluate(Ctx(new FakeWmiReader())).Should().BeEmpty();
    }

    [Fact]
    public void RecordingSpace_WarnsBelow50Gb()
    {
        var low = new FakeFileSystem();
        low.Drives[@"C:\"] = new DriveSpace(500L * 1024 * 1024 * 1024, 30L * 1024 * 1024 * 1024);
        var ok = new FakeFileSystem();
        ok.Drives[@"C:\"] = new DriveSpace(500L * 1024 * 1024 * 1024, 200L * 1024 * 1024 * 1024);

        new StreamingRecordingSpaceRule().Evaluate(Ctx(fs: low)).Should().ContainSingle(f => f.Evidence!.Contains("30") && f.Message.Contains("22 GB"));
        new StreamingRecordingSpaceRule().Evaluate(Ctx(fs: ok)).Should().BeEmpty();
    }

    [Fact]
    public void EncoderGuide_DependsOnVendor()
    {
        var nvidia = new SystemProfile { GpuNames = ["NVIDIA GeForce RTX 4070 SUPER"] };
        new StreamingEncoderGuide().Evaluate(Ctx(profile: nvidia)).Single().Message.Should().Contain("NVENC").And.Contain("CPU");
        new StreamingEncoderGuide().Evaluate(Ctx(profile: new SystemProfile { GpuNames = ["AMD Radeon(TM) Graphics"] })).Single().Message.Should().Contain("AMF");
    }

    [Fact]
    public void Guides_DoNotInventNumbersOrPromiseGains()
    {
        var all = new Analyzer(StreamingChecks.Rules()).Run(Ctx(Adapters(("Realtek PCIe GbE Family Controller", 2, 1_000_000_000))));

        all.Should().NotContain(f => f.Severity == Severity.Error);
        all.Should().OnlyContain(f => f.Category == FindingCategory.Streaming);
        all.Select(f => f.Id).Should().Contain(["streaming-network", "streaming-encoder-guide", "streaming-guide-obs", "streaming-guide-session"]);
        all.Should().NotContain(f => f.Message.Contains('%') && f.Message.Contains("ganho"));
    }

    [Fact]
    public void Classifier_FlagsSaturatedEncoder_WithoutChangingPrimaryBottleneck()
    {
        var samples = Enumerable.Range(0, 60).Select(i => new TelemetrySample(DateTimeOffset.UnixEpoch.AddSeconds(i),
            new SensorReading { GpuUtilizationPercent = 60, GpuEncoderPercent = 95, CpuTotalPercent = 30, CpuMaxThreadPercent = 40 })).ToList();

        var report = new BottleneckClassifier().Classify(samples);

        report.Findings.Should().ContainSingle(f => f.Id == "streaming-encoder-saturated");
        report.Primary.Should().Be(Bottleneck.None);
        report.Evidence.Should().Contain(e => e.StartsWith("Codificador de vídeo", StringComparison.Ordinal));
    }

    [Fact]
    public void Classifier_NoEncoderFindingWhenIdleOrUnavailable()
    {
        var idle = Enumerable.Range(0, 60).Select(i => new TelemetrySample(DateTimeOffset.UnixEpoch.AddSeconds(i), new SensorReading { GpuEncoderPercent = 10 })).ToList();
        var missing = Enumerable.Range(0, 60).Select(i => new TelemetrySample(DateTimeOffset.UnixEpoch.AddSeconds(i), new SensorReading())).ToList();

        new BottleneckClassifier().Classify(idle).Findings.Should().BeEmpty();
        new BottleneckClassifier().Classify(missing).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Focus.Streaming, true, true)]
    [InlineData(Focus.Gaming, true, false)]
    [InlineData(Focus.Both, true, false)]
    [InlineData(Focus.Work, false, false)]
    public void FocusExtensions(Focus focus, bool includesGaming, bool includesStreaming)
    {
        focus.IncludesGaming().Should().Be(includesGaming);
        focus.IncludesStreaming().Should().Be(includesStreaming);
    }

    [Fact]
    public void StreamingFocus_GetsGamingTweaks_NotWorkOnly_AndStreamDropsPicksGameDvr()
    {
        var catalog = new TweakCatalog(new FakeRegistry(), new FakeProcessRunner());
        var engine = new RecommendationEngine();

        var all = engine.Recommend(catalog.All, new RecommendationRequest(Focus.Streaming, Scenario.None, GameTarget.None, SystemProfile.Unknown))
            .Select(r => r.Tweak.Id).ToList();
        var drops = engine.Recommend(catalog.All, new RecommendationRequest(Focus.Streaming, Scenario.StreamDrops, GameTarget.None, SystemProfile.Unknown))
            .Select(r => r.Tweak.Id).ToList();
        var gaming = engine.Recommend(catalog.All, new RecommendationRequest(Focus.Gaming, Scenario.StreamDrops, GameTarget.None, SystemProfile.Unknown))
            .Select(r => r.Tweak.Id).ToList();

        all.Should().Contain(["gamedvr-off", "game-mode", "power-high", "startup-delay"]).And.NotContain("transparency-off");
        drops.Should().Equal("gamedvr-off");
        gaming.Should().Equal("gamedvr-off");
    }

    [Fact]
    public void StreamingFocus_KeepsGameFiltering()
    {
        var catalog = new TweakCatalog(new FakeRegistry(), new FakeProcessRunner());

        var recs = new RecommendationEngine().Recommend(catalog.All,
            new RecommendationRequest(Focus.Streaming, Scenario.None, GameTarget.Cs2, SystemProfile.Unknown));

        recs.Should().NotBeEmpty();
    }
}
