using FluentAssertions;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using Xunit;
using Xunit.Abstractions;

namespace WinTune.Platform.Tests;

/// <summary>Executa as partes somente leitura do Core contra o Windows real. Fora do CI.</summary>
[Trait("Category", "Integration")]
public class CoreOnRealMachineTests(ITestOutputHelper output)
{
    [Fact]
    public void StaticDiagnostics_RunWithoutExceptions_AndWithoutWrites()
    {
        var wmi = new WmiReader();
        var registry = new ReadOnlyRegistryGuard(new WindowsRegistry());
        var runner = new ReadOnlyProcessGuard(new ProcessRunner());
        var env = new WindowsEnvironment();
        var profile = SystemProfileDetector.Detect(wmi, runner, env);
        var ctx = new DiagnosticContext(wmi, registry, new WindowsFileSystem(), env, new SystemClock(), profile);

        var findings = new Analyzer(Analyzer.StaticRules()).Run(ctx);

        output.WriteLine($"CPU: {profile.CpuName} | GPUs: {string.Join(", ", profile.GpuNames)} | RAM: {profile.TotalRamBytes / 1024 / 1024 / 1024} GB | Ryzen plan: {profile.HasAmdRyzenBalancedPlan}");
        foreach (var f in findings)
        {
            output.WriteLine($"[{f.Severity}] {f.Id}: {f.Title} — {f.Evidence}");
        }

        findings.Should().NotContain(f => f.Severity == Severity.Error);
    }

    [Fact]
    public void StartupManager_ListsRealItems_ReadOnly()
    {
        var manager = new Core.Startup.StartupManager(new ReadOnlyRegistryGuard(new WindowsRegistry()), new WindowsFileSystem(),
            new WindowsEnvironment(), new ScheduledTasks());

        var items = manager.List();

        foreach (var i in items)
        {
            output.WriteLine($"{i.Source} | {i.Name} | {i.Publisher} | {i.State} | {i.Impact} | {i.BlockedReason}");
        }

        items.Should().NotBeNull();
    }

    [Fact]
    public void CleanupPreview_OnRealFileSystem_DoesNotDelete()
    {
        var service = new Core.Cleanup.CleanupService(new WindowsFileSystem(), new SystemClock(), new WindowsEnvironment());

        var plan = service.Preview(["user-temp", "thumbnail-cache", "wer-reports", "shader-cache"]);

        output.WriteLine($"Candidatos: {plan.Candidates.Count}, {plan.TotalBytes / 1024 / 1024} MB; ignorados: {plan.Skipped.Count}");
        foreach (var (cat, bytes) in plan.BytesByCategory)
        {
            output.WriteLine($"{cat}: {bytes / 1024 / 1024} MB");
        }

        foreach (var reason in plan.Skipped.GroupBy(s => s.Reason))
        {
            output.WriteLine($"ignorado ({reason.Key}): {reason.Count()}");
        }

        plan.Candidates.Should().OnlyContain(c => File.Exists(c.Path));
    }

    [Fact]
    public void ServiceAudit_OnRealMachine_NoMicrosoftServiceIsChangeable()
    {
        var wmi = new WmiReader();
        var auditor = new Core.Services.ServiceAuditor(new ServiceControl(wmi, new ReadOnlyRegistryGuard(new WindowsRegistry())),
            new ScheduledTasks(), new WindowsFileSystem(), new WindowsEnvironment(), new Sensors.ProcessUsageReader(wmi));

        var services = auditor.ListServices();
        output.WriteLine($"Serviços: {services.Count}; alteráveis: {string.Join(", ", services.Where(s => s.CanSetManual).Select(s => s.Info.Name))}");
        output.WriteLine($"Tarefas de atualizador: {string.Join(", ", auditor.ListUpdaterTasks().Select(t => t.Task.Path))}");

        services.Should().NotBeEmpty();
        services.Where(s => s.IsMicrosoft).Should().OnlyContain(s => !s.CanSetManual);
        services.Where(s => s.CanSetManual).Select(s => s.Info.Name)
            .Should().BeSubsetOf(Core.Services.ServiceAuditor.ServiceAllowlist.Keys);
    }

    [Fact]
    public void Catalog_IsAppliedAndDescribeChanges_AreReadOnly()
    {
        var catalog = new TweakCatalog(new ReadOnlyRegistryGuard(new WindowsRegistry()), new ReadOnlyProcessGuard(new ProcessRunner()));
        foreach (var t in catalog.All)
        {
            output.WriteLine($"{t.Id}: aplicado={t.IsApplied()} | {string.Join(" | ", t.DescribeChanges())}");
        }
    }
}
