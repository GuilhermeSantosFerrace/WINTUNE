using FluentAssertions;
using WinTune.Core.Domain;
using WinTune.Core.Services;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

public class ServiceAuditorTests
{
    private static (ServiceAuditor Auditor, FakeServiceControl Services, FakeScheduledTasks Tasks) World()
    {
        var services = new FakeServiceControl()
            .Add("AdobeARMservice", ServiceStartMode.Automatic, @"""C:\Program Files (x86)\Common Files\Adobe\ARM\1.0\armsvc.exe""", delayed: true, pid: 40)
            .Add("wuauserv", ServiceStartMode.Manual, @"C:\Windows\system32\svchost.exe -k netsvcs -p", pid: 8)
            .Add("Spooler", ServiceStartMode.Automatic, @"C:\Windows\System32\spoolsv.exe", pid: 9)
            .Add("gupdate", ServiceStartMode.Automatic, @"""C:\Fake\GoogleUpdate.exe"" /svc")
            .Add("RandomVendorSvc", ServiceStartMode.Automatic, @"C:\Vendor\svc.exe");
        var fs = new FakeFileSystem()
            .AddFile(@"C:\Program Files (x86)\Common Files\Adobe\ARM\1.0\armsvc.exe", company: "Adobe Inc.")
            .AddFile(@"C:\Fake\GoogleUpdate.exe", company: "Microsoft Corporation") // simula binário assinado pela Microsoft com nome da allowlist
            .AddFile(@"C:\Vendor\svc.exe", company: "Vendor");
        var tasks = new FakeScheduledTasks()
            .Add(@"\GoogleUpdateTaskMachineUA", author: "Google LLC")
            .Add(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan", author: "Microsoft Corporation")
            .Add(@"\GoogleUpdateTaskMachineFake", author: "Microsoft Corporation");
        var usage = new FakeProcessUsageReader();
        usage.Usage.Add(new ProcessUsage("armsvc", 40, 0.1, 3, 0));
        return (new ServiceAuditor(services, tasks, fs, new FakeEnvironment(), usage), services, tasks);
    }

    [Fact]
    public void MicrosoftAndUnclassifiedServices_AreNeverChangeable()
    {
        var (auditor, _, _) = World();
        var list = auditor.ListServices();

        list.Single(s => s.Info.Name == "wuauserv").Should().Match<AuditedService>(s => s.IsMicrosoft && !s.CanSetManual);
        list.Single(s => s.Info.Name == "Spooler").Should().Match<AuditedService>(s => s.IsMicrosoft && !s.CanSetManual);
        list.Single(s => s.Info.Name == "gupdate").CanSetManual.Should().BeFalse("binário da Microsoft nunca é alterável, mesmo com nome da allowlist");
        list.Single(s => s.Info.Name == "RandomVendorSvc").CanSetManual.Should().BeFalse("não classificado = somente leitura");
        list.Single(s => s.Info.Name == "AdobeARMservice").Should().Match<AuditedService>(s => s.CanSetManual && s.MemoryMb == 3);

        auditor.CreateServiceTweak("Spooler").Should().BeNull();
        auditor.CreateServiceTweak("wuauserv").Should().BeNull();
        auditor.CreateServiceTweak("gupdate").Should().BeNull();
        auditor.Resolve("service-manual:Spooler").Should().BeNull("a reconstrução só aceita nomes da allowlist em código");
    }

    [Fact]
    public void SetManual_ThenRevert_RestoresExactStartType()
    {
        var (auditor, services, _) = World();
        var (store, _, _) = TestHelpers.NewStore();

        var tweak = auditor.CreateServiceTweak("AdobeARMservice")!;
        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        services.Services["AdobeARMservice"].Config.Should().Be(new ServiceConfig(ServiceStartMode.Manual, false));
        services.Services["AdobeARMservice"].State.Should().Be("Running", "nunca para o serviço");

        auditor.Resolve(tweak.Id)!.Revert(store).Status.Should().Be(TweakStatus.Success);
        services.Services["AdobeARMservice"].Config.Should().Be(new ServiceConfig(ServiceStartMode.Automatic, true));
        store.HasBackup(tweak.Id).Should().BeFalse();
    }

    [Fact]
    public void Revert_WithDrift_IsConflict()
    {
        var (auditor, services, _) = World();
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = auditor.CreateServiceTweak("AdobeARMservice")!;
        tweak.Apply(store);
        services.SetConfig("AdobeARMservice", new ServiceConfig(ServiceStartMode.Disabled, false));

        tweak.Revert(store).Status.Should().Be(TweakStatus.Conflict);
        services.Services["AdobeARMservice"].Config.StartMode.Should().Be(ServiceStartMode.Disabled);
        tweak.Revert(store, RevertMode.Force).Status.Should().Be(TweakStatus.Success);
        services.Services["AdobeARMservice"].Config.Should().Be(new ServiceConfig(ServiceStartMode.Automatic, true));
    }

    [Fact]
    public void UpdaterTasks_OnlyAllowlistedNonMicrosoft_AndReversible()
    {
        var (auditor, _, tasks) = World();
        var (store, _, _) = TestHelpers.NewStore();

        var list = auditor.ListUpdaterTasks();
        list.Should().ContainSingle(t => t.Task.Path == @"\GoogleUpdateTaskMachineUA");
        auditor.CreateTaskTweak(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan").Should().BeNull();

        var tweak = auditor.CreateTaskTweak(@"\GoogleUpdateTaskMachineUA")!;
        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        tasks.Tasks[@"\GoogleUpdateTaskMachineUA"].Enabled.Should().BeFalse();

        auditor.Resolve(tweak.Id)!.Revert(store).Status.Should().Be(TweakStatus.Success);
        tasks.Tasks[@"\GoogleUpdateTaskMachineUA"].Enabled.Should().BeTrue();
    }
}
