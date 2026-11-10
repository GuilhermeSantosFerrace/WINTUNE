using FluentAssertions;
using WinTune.Platform.Abstractions;
using Xunit;

namespace WinTune.Platform.Tests;

/// <summary>
/// Testes de integração SOMENTE LEITURA contra o Windows real. Fora do CI (filtro Category!=Integration).
/// </summary>
[Trait("Category", "Integration")]
public class IntegrationTests
{
    [Fact]
    public void Registry_ReadsWindowsProductName()
    {
        new WindowsRegistry()
            .GetString(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName")
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ProcessRunner_RunsHostname()
    {
        var result = new ProcessRunner().Run("hostname.exe", [], TimeSpan.FromSeconds(10));
        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void Wmi_ReadsOperatingSystem()
    {
        new WmiReader().Query(WmiScopes.Cimv2, "SELECT Caption FROM Win32_OperatingSystem").Should().NotBeEmpty();
    }

    [Fact]
    public void Services_AreListed()
    {
        new ServiceControl(new WmiReader(), new WindowsRegistry()).GetServices().Should().NotBeEmpty();
    }

    [Fact]
    public void ProcessList_IsNotEmpty()
    {
        new ProcessList().GetProcessNames().Should().NotBeEmpty();
    }
}
