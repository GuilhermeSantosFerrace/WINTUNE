using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinTune.Core.Domain;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Games;

/// <summary>Conteúdo do <c>games.json</c> (editável pelo usuário, validado ao carregar).</summary>
public sealed class GamesConfigDocument
{
    /// <summary>Versão do schema.</summary>
    public int Schema { get; set; } = 1;

    /// <summary>Jogos.</summary>
    public List<GameEntry> Games { get; set; } = [];

    /// <summary>Processos conhecidos em segundo plano (nome sem .exe → categoria).</summary>
    public Dictionary<string, string> KnownBackgroundProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Limiar de RAM livre para o aviso de folga (GB).</summary>
    public double RamHeadroomGb { get; set; } = 6;
}

/// <summary>Jogo no <c>games.json</c>.</summary>
public sealed class GameEntry
{
    /// <summary>Jogo (RainbowSixSiege ou Cs2).</summary>
    public GameTarget Target { get; set; }

    /// <summary>Nome.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Processos (sem caminho).</summary>
    public List<string> ProcessNames { get; set; } = [];

    /// <summary>App ID da Steam.</summary>
    public int SteamAppId { get; set; }

    /// <summary>Executável relativo à instalação.</summary>
    public string ExecutableRelativePath { get; set; } = string.Empty;
}

/// <summary>Resultado do carregamento.</summary>
/// <param name="Games">Jogos válidos.</param>
/// <param name="KnownBackgroundProcesses">Processos conhecidos.</param>
/// <param name="RamHeadroomBytes">Limiar de RAM.</param>
/// <param name="Warning">Aviso (arquivo inválido; padrões em uso).</param>
public sealed record GamesConfig(IReadOnlyList<GameDefinition> Games, IReadOnlyDictionary<string, string> KnownBackgroundProcesses, ulong RamHeadroomBytes, string? Warning);

/// <summary>Carrega/cria o <c>games.json</c> em <c>%APPDATA%\WinTune</c>.</summary>
public static partial class GamesConfigLoader
{
    /// <summary>Tamanho máximo.</summary>
    public const long MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Caminho padrão.</summary>
    public static string DefaultPath(IEnvironmentInfo env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return Path.Combine(env.GetFolder(KnownFolder.RoamingAppData), "WinTune", "games.json");
    }

    /// <summary>Configuração padrão.</summary>
    public static GamesConfig Defaults { get; } =
        new(GameDefinition.Defaults, BackgroundLoadInspector.DefaultKnownProcesses, 6UL * 1024 * 1024 * 1024, null);

    /// <summary>Carrega; se não existir, grava o padrão. Arquivo inválido nunca é sobrescrito: usa o padrão e avisa.</summary>
    public static GamesConfig Load(IFileSystem fs, string path)
    {
        ArgumentNullException.ThrowIfNull(fs);
        if (!fs.FileExists(path))
        {
            fs.WriteAllTextAtomic(path, JsonSerializer.Serialize(ToDocument(Defaults), Json));
            return Defaults;
        }

        try
        {
            if (fs.GetFile(path)?.Length > MaxBytes)
            {
                throw new InvalidDataException("arquivo grande demais");
            }

            var doc = JsonSerializer.Deserialize<GamesConfigDocument>(fs.ReadAllText(path), Json) ?? throw new InvalidDataException("vazio");
            return Validate(doc);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            return Defaults with { Warning = $"games.json inválido ({ex.Message}); usando a configuração padrão. Corrija o arquivo em {path}." };
        }
    }

    /// <summary>Valida o documento (schema, nomes de processo, limites).</summary>
    public static GamesConfig Validate(GamesConfigDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Schema != 1)
        {
            throw new InvalidDataException($"schema {doc.Schema} não suportado");
        }

        if (doc.Games is null || doc.Games.Count is 0 or > 8)
        {
            throw new InvalidDataException("lista de jogos vazia ou grande demais");
        }

        var games = new List<GameDefinition>();
        foreach (var g in doc.Games)
        {
            if (g.Target is not (GameTarget.RainbowSixSiege or GameTarget.Cs2))
            {
                throw new InvalidDataException($"jogo desconhecido: {g.Target}");
            }

            if (g.ProcessNames is null || g.ProcessNames.Count is 0 or > 10 || g.ProcessNames.Any(p => !ProcessNameRegex().IsMatch(p ?? string.Empty)))
            {
                throw new InvalidDataException($"nomes de processo inválidos em {g.Target}");
            }

            if (string.IsNullOrWhiteSpace(g.ExecutableRelativePath) || g.ExecutableRelativePath.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(g.ExecutableRelativePath) || g.ExecutableRelativePath.Length > 260)
            {
                throw new InvalidDataException($"executável inválido em {g.Target}");
            }

            games.Add(new GameDefinition
            {
                Target = g.Target,
                DisplayName = string.IsNullOrWhiteSpace(g.DisplayName) ? g.Target.DisplayName() : g.DisplayName.Trim()[..Math.Min(g.DisplayName.Trim().Length, 64)],
                ProcessNames = g.ProcessNames.ToList(),
                SteamAppId = g.SteamAppId is >= 0 and < 100_000_000 ? g.SteamAppId : 0,
                ExecutableRelativePath = g.ExecutableRelativePath,
            });
        }

        var known = (doc.KnownBackgroundProcesses ?? [])
            .Where(kv => ProcessNameRegex().IsMatch(kv.Key) && kv.Value is { Length: > 0 and <= 64 })
            .Take(200)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var ram = doc.RamHeadroomGb is > 0 and <= 128 ? doc.RamHeadroomGb : 6;
        return new GamesConfig(games, known, (ulong)(ram * 1024 * 1024 * 1024), null);
    }

    private static GamesConfigDocument ToDocument(GamesConfig c) => new()
    {
        Games = c.Games.Select(g => new GameEntry
        {
            Target = g.Target,
            DisplayName = g.DisplayName,
            ProcessNames = g.ProcessNames.ToList(),
            SteamAppId = g.SteamAppId,
            ExecutableRelativePath = g.ExecutableRelativePath,
        }).ToList(),
        KnownBackgroundProcesses = new Dictionary<string, string>(c.KnownBackgroundProcesses, StringComparer.OrdinalIgnoreCase),
        RamHeadroomGb = c.RamHeadroomBytes / 1024d / 1024 / 1024,
    };

    [GeneratedRegex(@"^[A-Za-z0-9_.\- ]{1,64}$")]
    private static partial Regex ProcessNameRegex();
}
