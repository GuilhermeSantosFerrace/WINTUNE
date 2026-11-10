using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WinTune.Core.Tests;

/// <summary>Regras de arquitetura verificadas sobre o código-fonte.</summary>
public partial class ArchitectureTests
{
    [Fact]
    public void NoDirectRegistryOrProcessAccess_OutsidePlatform()
    {
        var offenders = SourceFiles()
            .Where(f => !f.Contains(@"\src\WinTune.Platform\", StringComparison.OrdinalIgnoreCase))
            .Where(f => ForbiddenWindowsApi().IsMatch(File.ReadAllText(f)))
            .ToList();

        offenders.Should().BeEmpty("somente WinTune.Platform pode tocar o registro ou iniciar processos");
    }

    [Fact]
    public void NoNetworkApis_AnywhereInSource()
    {
        var offenders = SourceFiles().Where(f => NetworkApi().IsMatch(File.ReadAllText(f))).ToList();
        offenders.Should().BeEmpty("o WinTune não faz chamadas de rede (seção 1.4)");
    }

    internal static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) && !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinTune.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WinTune.sln não encontrado.");
    }

    [GeneratedRegex(@"Microsoft\.Win32\.Registry\b|\bRegistry\.(LocalMachine|CurrentUser)|RegistryKey\.OpenBaseKey|Process\.Start\(|new\s+Process\s*\{|new\s+ProcessStartInfo\b")]
    private static partial Regex ForbiddenWindowsApi();

    [GeneratedRegex(@"\bHttpClient\b|\bWebClient\b|\bWebRequest\b|\bTcpClient\b|\bUdpClient\b|System\.Net\.Sockets|\bHttpWebRequest\b")]
    private static partial Regex NetworkApi();
}
