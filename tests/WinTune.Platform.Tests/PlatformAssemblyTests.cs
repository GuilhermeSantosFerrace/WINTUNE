using FluentAssertions;
using Xunit;

namespace WinTune.Platform.Tests;

public class PlatformAssemblyTests
{
    [Fact]
    public void LayerName_IsPlatform()
    {
        PlatformAssembly.LayerName.Should().Be("Platform");
    }
}
