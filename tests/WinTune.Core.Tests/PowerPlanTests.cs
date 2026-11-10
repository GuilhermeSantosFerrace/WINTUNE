using FluentAssertions;
using WinTune.Core.Domain;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.Core.Tests;

/// <summary>Simula o powercfg com estado (planos e plano ativo), com saída em EN-US ou PT-BR.</summary>
internal sealed class PowerCfgSimulator
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string RyzenBalanced = "9897998c-92de-4669-853f-b7cd3ecb2790";

    private readonly bool _portuguese;

    public PowerCfgSimulator(bool portuguese, bool includeHighPerformance = true, bool includeRyzen = false)
    {
        _portuguese = portuguese;
        Schemes.Add((Balanced, portuguese ? "Equilibrado" : "Balanced"));
        if (includeHighPerformance)
        {
            Schemes.Add((PowerCfg.HighPerformanceGuid, portuguese ? "Alto desempenho" : "High performance"));
        }

        if (includeRyzen)
        {
            Schemes.Add((RyzenBalanced, "AMD Ryzen™ Balanced"));
        }

        Active = Balanced;
        Runner = new FakeProcessRunner()
            .On(PowerCfg.Exe, ["/getactivescheme"], _ => Ok($"{Label} {Active}  ({Name(Active)})"))
            .On(PowerCfg.Exe, ["/list"], _ => Ok(ListOutput()))
            .On(PowerCfg.Exe, ["/setactive"], args =>
            {
                if (Schemes.All(s => s.Guid != args[1]))
                {
                    return new ProcessResult(1, string.Empty, "Invalid", false);
                }

                Active = args[1];
                return Ok(string.Empty);
            })
            .On(PowerCfg.Exe, ["/duplicatescheme"], _ =>
            {
                Schemes.Add((Duplicated, _portuguese ? "Alto desempenho" : "High performance"));
                return Ok($"{Label} {Duplicated}  ({Name(Duplicated)})");
            })
            .On(PowerCfg.Exe, ["/delete"], args =>
            {
                Schemes.RemoveAll(s => s.Guid == args[1]);
                return Ok(string.Empty);
            });
    }

    public const string Duplicated = "11111111-2222-3333-4444-555555555555";

    public List<(string Guid, string Name)> Schemes { get; } = [];

    public string Active { get; set; }

    public FakeProcessRunner Runner { get; }

    private string Label => _portuguese ? "GUID do Esquema de Energia:" : "Power Scheme GUID:";

    private string Name(string guid) => Schemes.First(s => s.Guid == guid).Name;

    private string ListOutput()
    {
        var header = _portuguese ? "Esquemas de Energia Existentes (* Ativo)" : "Existing Power Schemes (* Active)";
        var lines = Schemes.Select(s => $"{Label} {s.Guid}  ({s.Name}){(s.Guid == Active ? " *" : string.Empty)}");
        return header + "\r\n-----------------------------------\r\n" + string.Join("\r\n", lines) + "\r\n";
    }

    private static ProcessResult Ok(string stdout) => new(0, stdout, string.Empty, false);
}

public class PowerPlanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parsing_WorksInEnglishAndPortuguese(bool portuguese)
    {
        var sim = new PowerCfgSimulator(portuguese, includeRyzen: true);

        PowerCfg.GetActive(sim.Runner).Should().Be(PowerCfgSimulator.Balanced);
        var list = PowerCfg.List(sim.Runner);
        list.Should().HaveCount(3);
        list.Single(s => s.Active).SchemeGuid.Should().Be(PowerCfgSimulator.Balanced);
        list.Should().ContainSingle(s => PowerCfg.IsAmdRyzenBalanced(s));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyThenRevert_RestoresExactPreviousGuid(bool portuguese)
    {
        var sim = new PowerCfgSimulator(portuguese);
        sim.Active = PowerCfgSimulator.Balanced;
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new PowerPlanTweak(sim.Runner);

        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        sim.Active.Should().Be(PowerCfg.HighPerformanceGuid);
        tweak.IsApplied().Should().BeTrue();

        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);
        sim.Active.Should().Be(PowerCfgSimulator.Balanced);
        store.HasBackup("power-high").Should().BeFalse();
    }

    [Fact]
    public void MissingHighPerformance_UsesDuplicateScheme_AndDeletesItOnRevert()
    {
        var sim = new PowerCfgSimulator(portuguese: true, includeHighPerformance: false);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new PowerPlanTweak(sim.Runner);

        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        sim.Active.Should().Be(PowerCfgSimulator.Duplicated);
        tweak.IsApplied().Should().BeTrue();

        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);
        sim.Active.Should().Be(PowerCfgSimulator.Balanced);
        sim.Schemes.Should().NotContain(s => s.Guid == PowerCfgSimulator.Duplicated);
    }

    [Fact]
    public void AlreadyActive_IsSkipped_WithoutBackup()
    {
        var sim = new PowerCfgSimulator(false) { Active = PowerCfg.HighPerformanceGuid };
        var (store, _, _) = TestHelpers.NewStore();

        new PowerPlanTweak(sim.Runner).Apply(store).Status.Should().Be(TweakStatus.Skipped);
        store.HasBackup("power-high").Should().BeFalse();
    }

    [Fact]
    public void Revert_WhenUserChangedPlan_IsConflict()
    {
        var sim = new PowerCfgSimulator(false, includeRyzen: true);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new PowerPlanTweak(sim.Runner);
        tweak.Apply(store);

        sim.Active = PowerCfgSimulator.RyzenBalanced;

        tweak.Revert(store).Status.Should().Be(TweakStatus.Conflict);
        sim.Active.Should().Be(PowerCfgSimulator.RyzenBalanced);
        tweak.Revert(store, RevertMode.Force).Status.Should().Be(TweakStatus.Success);
        sim.Active.Should().Be(PowerCfgSimulator.Balanced);
    }

    [Fact]
    public void Revert_RefusesGuidUnknownToPowercfg()
    {
        var sim = new PowerCfgSimulator(false);
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new PowerPlanTweak(sim.Runner);
        tweak.Apply(store);
        sim.Schemes.RemoveAll(s => s.Guid == PowerCfgSimulator.Balanced);

        tweak.Revert(store).Status.Should().Be(TweakStatus.Failed);
    }

    [Fact]
    public void RyzenBalancedPlan_MakesItOptIn()
    {
        var tweak = new PowerPlanTweak(new FakeProcessRunner());
        tweak.GetAdvice(new SystemProfile { HasAmdRyzenBalancedPlan = true }).DefaultSelected.Should().BeFalse();
        tweak.GetAdvice(new SystemProfile()).DefaultSelected.Should().BeTrue();
    }

    [Fact]
    public void GuidArguments_AreValidated()
    {
        var act = () => PowerCfg.RunWithGuid(new FakeProcessRunner(), "/setactive", "abc & del *");
        act.Should().Throw<ArgumentException>();
    }
}

public class FlipModelTests
{
    private static RegistryValue? Current(FakeRegistry reg) =>
        reg.GetValue(RegistryHive.CurrentUser, FlipModelUpgradeTweak.UserGpuPreferencesKey, FlipModelUpgradeTweak.GlobalSettingsName);

    [Fact]
    public void Apply_PreservesOtherFlags_AndRevertRestoresOnlyOurFlag()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, FlipModelUpgradeTweak.UserGpuPreferencesKey,
            FlipModelUpgradeTweak.GlobalSettingsName, "VRROptimizeEnable=0;AutoHDREnable=1;");
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new FlipModelUpgradeTweak(reg);

        tweak.Apply(store).Status.Should().Be(TweakStatus.Success);
        Current(reg)!.Text.Should().Be("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=1;");

        // Usuário muda outra flag depois; reverter preserva essa mudança.
        reg.SetString(RegistryHive.CurrentUser, FlipModelUpgradeTweak.UserGpuPreferencesKey, FlipModelUpgradeTweak.GlobalSettingsName,
            "VRROptimizeEnable=1;AutoHDREnable=1;SwapEffectUpgradeEnable=1;");

        tweak.Revert(store).Status.Should().Be(TweakStatus.Success);
        Current(reg)!.Text.Should().Be("VRROptimizeEnable=1;AutoHDREnable=1;");
    }

    [Fact]
    public void Revert_DeletesValue_WhenItDidNotExist()
    {
        var reg = new FakeRegistry();
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new FlipModelUpgradeTweak(reg);

        tweak.Apply(store);
        Current(reg)!.Text.Should().Be("SwapEffectUpgradeEnable=1;");
        tweak.Revert(store);

        Current(reg).Should().BeNull();
    }

    [Fact]
    public void ExistingFlagZero_IsRestoredToZero()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, FlipModelUpgradeTweak.UserGpuPreferencesKey,
            FlipModelUpgradeTweak.GlobalSettingsName, "SwapEffectUpgradeEnable=0;HighPerfAdapter=10DE&2783;");
        var (store, _, _) = TestHelpers.NewStore();
        var tweak = new FlipModelUpgradeTweak(reg);

        tweak.Apply(store);
        Current(reg)!.Text.Should().Be("SwapEffectUpgradeEnable=1;HighPerfAdapter=10DE&2783;");
        tweak.Revert(store);
        Current(reg)!.Text.Should().Be("SwapEffectUpgradeEnable=0;HighPerfAdapter=10DE&2783;");
    }

    [Fact]
    public void UnexpectedValueType_FailsWithoutWriting()
    {
        var reg = new FakeRegistry().With(RegistryHive.CurrentUser, FlipModelUpgradeTweak.UserGpuPreferencesKey,
            FlipModelUpgradeTweak.GlobalSettingsName, 1);
        var (store, _, _) = TestHelpers.NewStore();

        new FlipModelUpgradeTweak(reg).Apply(store).Status.Should().Be(TweakStatus.Failed);
        Current(reg)!.DWord.Should().Be(1);
    }

    [Fact]
    public void IsOptIn()
    {
        new FlipModelUpgradeTweak(new FakeRegistry()).DefaultSelected.Should().BeFalse();
    }
}
