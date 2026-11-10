using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Games;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Profiles;

/// <summary>Perfil salvo (nome, foco, cenários, jogos e ajustes selecionados).</summary>
public sealed record UserProfile
{
    /// <summary>Schema.</summary>
    public int Schema { get; init; } = 1;

    /// <summary>Nome.</summary>
    public required string Name { get; init; }

    /// <summary>Foco.</summary>
    public Focus Focus { get; init; }

    /// <summary>Cenários.</summary>
    public Scenario Scenarios { get; init; }

    /// <summary>Jogos.</summary>
    public GameTarget Games { get; init; }

    /// <summary>Ajustes selecionados.</summary>
    public IReadOnlyList<string> TweakIds { get; init; } = [];
}

/// <summary>Configuração do modo jogo automático (opt-in).</summary>
public sealed record GameModeSettings
{
    /// <summary>Schema.</summary>
    public int Schema { get; init; } = 1;

    /// <summary>Ligado (padrão: desligado).</summary>
    public bool Enabled { get; init; }

    /// <summary>Ajustes por jogo (rótulo <c>siege</c>/<c>cs2</c> → IDs do catálogo).</summary>
    public Dictionary<string, List<string>> PerGame { get; init; } = new(StringComparer.Ordinal)
    {
        ["siege"] = ["power-high"],
        ["cs2"] = ["power-high"],
    };
}

/// <summary>Estado do modo jogo em andamento (gravado antes de agir; sobra após queda).</summary>
public sealed record GameModeState
{
    /// <summary>Jogo.</summary>
    public required string Game { get; init; }

    /// <summary>Início.</summary>
    public DateTimeOffset StartedUtc { get; init; }

    /// <summary>Ajustes aplicados pelo modo jogo (somente estes são revertidos ao sair).</summary>
    public List<string> AppliedTweakIds { get; init; } = [];
}

/// <summary>Persistência de perfis, configuração e estado do modo jogo em <c>%APPDATA%\WinTune</c>.</summary>
public sealed partial class ProfileStore
{
    /// <summary>Tamanho máximo de arquivo.</summary>
    public const long MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly IFileSystem _fs;
    private readonly string _root;

    /// <summary>Cria o repositório.</summary>
    public ProfileStore(IFileSystem fs, IEnvironmentInfo env)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _root = Path.Combine((env ?? throw new ArgumentNullException(nameof(env))).GetFolder(KnownFolder.RoamingAppData), "WinTune");
    }

    /// <summary>Pasta de perfis.</summary>
    public string ProfilesDirectory => Path.Combine(_root, "profiles");

    private string SettingsPath => Path.Combine(_root, "gamemode.json");

    private string StatePath => Path.Combine(_root, "gamemode-state.json");

    /// <summary>Lista perfis válidos (inválidos são ignorados).</summary>
    public IReadOnlyList<UserProfile> ListProfiles()
    {
        if (!_fs.DirectoryExists(ProfilesDirectory))
        {
            return [];
        }

        var list = new List<UserProfile>();
        foreach (var f in _fs.EnumerateFiles(ProfilesDirectory, "*.json", recursive: false))
        {
            var p = Read<UserProfile>(f.FullPath);
            if (p is not null && p.Schema == 1 && IsValidName(p.Name) && p.TweakIds.Count <= 200)
            {
                list.Add(p);
            }
        }

        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Salva (ou substitui) um perfil.</summary>
    public void SaveProfile(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsValidName(profile.Name))
        {
            throw new ArgumentException("Nome de perfil inválido (use letras, números, espaço, - e _; até 40 caracteres).", nameof(profile));
        }

        _fs.CreateDirectory(ProfilesDirectory);
        _fs.WriteAllTextAtomic(ProfilePath(profile.Name), JsonSerializer.Serialize(profile, Json));
    }

    /// <summary>Apaga um perfil.</summary>
    public void DeleteProfile(string name)
    {
        if (IsValidName(name) && _fs.FileExists(ProfilePath(name)))
        {
            _fs.DeleteFile(ProfilePath(name));
        }
    }

    /// <summary>Configuração do modo jogo (padrão desligado).</summary>
    public GameModeSettings LoadSettings() => Read<GameModeSettings>(SettingsPath) is { Schema: 1 } s ? s : new GameModeSettings();

    /// <summary>Grava a configuração do modo jogo.</summary>
    public void SaveSettings(GameModeSettings settings) => _fs.WriteAllTextAtomic(SettingsPath, JsonSerializer.Serialize(settings, Json));

    /// <summary>Estado pendente (não nulo após queda com modo jogo ativo).</summary>
    public GameModeState? LoadState() => Read<GameModeState>(StatePath);

    /// <summary>Grava o estado.</summary>
    public void SaveState(GameModeState state) => _fs.WriteAllTextAtomic(StatePath, JsonSerializer.Serialize(state, Json));

    /// <summary>Remove o estado.</summary>
    public void ClearState()
    {
        if (_fs.FileExists(StatePath))
        {
            _fs.DeleteFile(StatePath);
        }
    }

    /// <summary>Valida o nome de perfil.</summary>
    public static bool IsValidName(string? name) => name is not null && NameRegex().IsMatch(name);

    private string ProfilePath(string name) => Path.Combine(ProfilesDirectory, name.Trim() + ".json");

    private T? Read<T>(string path)
        where T : class
    {
        try
        {
            if (!_fs.FileExists(path) || _fs.GetFile(path)?.Length > MaxBytes)
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(_fs.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[\p{L}0-9 _-]{1,40}$")]
    private static partial Regex NameRegex();
}

/// <summary>Evento do orquestrador.</summary>
/// <param name="Message">Mensagem pt-BR.</param>
public sealed record GameModeEvent(string Message);

/// <summary>
/// Modo jogo automático: detecta o jogo apenas pela existência do processo, aplica os ajustes do perfil do jogo ao abrir e
/// restaura ao fechar (mesmos <see cref="Tweak"/>, mesmo backup/journal). Ajustes que o usuário já aplicou manualmente não são tocados.
/// </summary>
public sealed class GameModeOrchestrator
{
    private readonly IProcessList _processes;
    private readonly IReadOnlyList<GameDefinition> _games;
    private readonly Func<string, Tweak?> _resolveCatalog;
    private readonly BackupStore _backup;
    private readonly ProfileStore _store;
    private readonly IClock _clock;

    /// <summary>Cria o orquestrador. <paramref name="resolveCatalog"/> deve resolver só IDs do catálogo.</summary>
    public GameModeOrchestrator(IProcessList processes, IReadOnlyList<GameDefinition> games, Func<string, Tweak?> resolveCatalog,
        BackupStore backup, ProfileStore store, IClock clock)
    {
        _processes = processes;
        _games = games;
        _resolveCatalog = resolveCatalog;
        _backup = backup;
        _store = store;
        _clock = clock;
    }

    /// <summary>Jogo ativo (rótulo) ou null.</summary>
    public string? ActiveGame => _store.LoadState()?.Game;

    /// <summary>Verifica os processos e entra/sai do modo jogo conforme necessário.</summary>
    public IReadOnlyList<GameModeEvent> Poll(GameModeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var events = new List<GameModeEvent>();
        var state = _store.LoadState();
        var names = _processes.GetProcessNames();
        var running = _games.FirstOrDefault(g => names.Any(g.MatchesProcess));

        if (state is not null && (running is null || running.Target.ToLabel() != state.Game))
        {
            events.AddRange(Exit(state));
            state = null;
        }

        if (state is null && running is not null && settings.Enabled)
        {
            events.AddRange(Enter(running, settings));
        }

        return events;
    }

    /// <summary>Restaura um estado pendente (queda com modo jogo ativo) ou ao desligar o modo.</summary>
    public IReadOnlyList<GameModeEvent> RestorePending()
    {
        var state = _store.LoadState();
        return state is null ? [] : Exit(state);
    }

    private List<GameModeEvent> Enter(GameDefinition game, GameModeSettings settings)
    {
        var label = game.Target.ToLabel();
        var events = new List<GameModeEvent> { new($"{game.DisplayName} detectado: ativando perfil de jogo.") };
        var state = new GameModeState { Game = label, StartedUtc = _clock.UtcNow };
        _store.SaveState(state); // intenção gravada antes de agir.

        foreach (var id in settings.PerGame.TryGetValue(label, out var ids) ? ids : [])
        {
            var tweak = _resolveCatalog(id);
            if (tweak is null)
            {
                events.Add(new($"{id}: ajuste desconhecido; ignorado."));
                continue;
            }

            if (_backup.HasBackup(id))
            {
                events.Add(new($"{tweak.Name}: já aplicado por você; o modo jogo não mexe nele."));
                continue;
            }

            var result = tweak.Apply(_backup);
            if (result.Status == TweakStatus.Success)
            {
                state.AppliedTweakIds.Add(id);
                _store.SaveState(state);
            }

            events.Add(new($"{tweak.Name}: {result.Message}"));
        }

        _backup.Journal("game-mode", label, "enter", string.Join(",", state.AppliedTweakIds));
        return events;
    }

    private List<GameModeEvent> Exit(GameModeState state)
    {
        var events = new List<GameModeEvent> { new($"Saindo do modo jogo ({state.Game}): restaurando o estado anterior.") };
        foreach (var id in Enumerable.Reverse(state.AppliedTweakIds))
        {
            var tweak = _resolveCatalog(id);
            if (tweak is null)
            {
                continue;
            }

            var r = tweak.Revert(_backup, RevertMode.Safe);
            events.Add(new($"{tweak.Name}: {r.Message}"));
        }

        _store.ClearState();
        _backup.Journal("game-mode", state.Game, "exit");
        return events;
    }
}
