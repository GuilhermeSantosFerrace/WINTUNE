using System.Text.RegularExpressions;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Games;

/// <summary>Localiza instalações (Steam: <c>libraryfolders.vdf</c>; Ubisoft Connect: registro). Somente leitura.</summary>
public sealed partial class GameInstallLocator
{
    /// <summary>Chave da Steam no HKCU.</summary>
    public const string SteamKey = @"Software\Valve\Steam";

    /// <summary>Chave de instalações do Ubisoft Connect.</summary>
    public const string UbisoftInstallsKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";

    private readonly IRegistry _registry;
    private readonly IFileSystem _fs;

    /// <summary>Cria o localizador.</summary>
    public GameInstallLocator(IRegistry registry, IFileSystem fs)
    {
        _registry = registry;
        _fs = fs;
    }

    /// <summary>Pastas de instalação encontradas para o jogo (confirmadas pela existência do executável).</summary>
    public IReadOnlyList<string> Locate(GameDefinition game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var found = new List<string>();
        if (game.SteamAppId > 0)
        {
            found.AddRange(SteamInstalls(game));
        }

        found.AddRange(UbisoftInstalls(game));
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Bibliotecas da Steam (inclui a pasta principal).</summary>
    public IReadOnlyList<string> SteamLibraries()
    {
        var steam = _registry.GetString(RegistryHive.CurrentUser, SteamKey, "SteamPath");
        if (string.IsNullOrWhiteSpace(steam))
        {
            return [];
        }

        steam = steam.Replace('/', '\\');
        var libs = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (_fs.FileExists(vdf))
        {
            libs.AddRange(ParseLibraryFolders(_fs.ReadAllText(vdf)));
        }

        return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Extrai os caminhos <c>"path"</c> do <c>libraryfolders.vdf</c>.</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdf) =>
        PathRegex().Matches(vdf ?? string.Empty).Select(m => m.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal)).ToList();

    /// <summary>Extrai <c>"installdir"</c> de um <c>appmanifest_*.acf</c>.</summary>
    public static string? ParseInstallDir(string acf)
    {
        var m = InstallDirRegex().Match(acf ?? string.Empty);
        return m.Success ? m.Groups[1].Value : null;
    }

    private IEnumerable<string> SteamInstalls(GameDefinition game)
    {
        foreach (var lib in SteamLibraries())
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{game.SteamAppId}.acf");
            if (!_fs.FileExists(manifest))
            {
                continue;
            }

            var dir = ParseInstallDir(_fs.ReadAllText(manifest));
            if (dir is null || dir.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            var install = Path.Combine(lib, "steamapps", "common", dir);
            if (_fs.FileExists(Path.Combine(install, game.ExecutableRelativePath)))
            {
                yield return install;
            }
        }
    }

    private IEnumerable<string> UbisoftInstalls(GameDefinition game)
    {
        foreach (var id in _registry.GetSubKeyNames(RegistryHive.LocalMachine, UbisoftInstallsKey))
        {
            var dir = _registry.GetString(RegistryHive.LocalMachine, $@"{UbisoftInstallsKey}\{id}", "InstallDir");
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            dir = dir.Replace('/', '\\').TrimEnd('\\');
            if (_fs.FileExists(Path.Combine(dir, game.ExecutableRelativePath)))
            {
                yield return dir;
            }
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathRegex();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();
}
