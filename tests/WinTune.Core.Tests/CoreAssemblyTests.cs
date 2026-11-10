using FluentAssertions;
using Xunit;

namespace WinTune.Core.Tests;

public class CoreAssemblyTests
{
    [Fact]
    public void LayerName_IsCore()
    {
        CoreAssembly.LayerName.Should().Be("Core");
    }

    [Fact]
    public void CoreAssembly_DoesNotReferencePlatformOrUi()
    {
        // A camada Core deve ser 100% testável: sem WPF e sem adaptadores do Windows.
        var referenced = typeof(CoreAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        referenced.Should().NotContain(new[] { "WinTune.Platform", "WinTune", "PresentationFramework" });
    }
}
