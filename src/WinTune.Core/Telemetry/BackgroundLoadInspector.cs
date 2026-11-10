using WinTune.Core.Games;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Telemetry;

/// <summary>Consumidor em segundo plano identificado.</summary>
/// <param name="Usage">Uso medido.</param>
/// <param name="KnownCategory">Categoria conhecida (overlay, RGB, navegador...) ou null.</param>
public sealed record BackgroundConsumer(ProcessUsage Usage, string? KnownCategory);

/// <summary>
/// <c>game-background-load</c>: lista os maiores consumidores e overlays conhecidos. Nunca finaliza processos.
/// Processos dos jogos são excluídos antes de qualquer análise (só o nome é comparado).
/// </summary>
public sealed class BackgroundLoadInspector
{
    /// <summary>Overlays e apps conhecidos (padrão; editável no <c>games.json</c> a partir da Fase 12).</summary>
    public static IReadOnlyDictionary<string, string> DefaultKnownProcesses { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Discord"] = "Overlay/comunicação",
        ["GameOverlayUI"] = "Overlay da Steam",
        ["steamwebhelper"] = "Steam (navegador interno)",
        ["NVIDIA Overlay"] = "Overlay da NVIDIA",
        ["GameBar"] = "Xbox Game Bar",
        ["GameBarFTServer"] = "Xbox Game Bar",
        ["iCUE"] = "Suíte de RGB",
        ["SignalRgb"] = "Suíte de RGB",
        ["obs64"] = "Gravador/transmissão",
        ["chrome"] = "Navegador",
        ["msedge"] = "Navegador",
        ["firefox"] = "Navegador",
        ["opera"] = "Navegador",
        ["brave"] = "Navegador",
    };

    private readonly IProcessUsageReader _reader;
    private readonly IReadOnlyList<GameDefinition> _games;
    private readonly IReadOnlyDictionary<string, string> _known;

    /// <summary>Cria o inspetor.</summary>
    public BackgroundLoadInspector(IProcessUsageReader reader, IReadOnlyList<GameDefinition> games, IReadOnlyDictionary<string, string>? known = null)
    {
        _reader = reader;
        _games = games;
        _known = known ?? DefaultKnownProcesses;
    }

    /// <summary>Maiores consumidores de CPU/RAM/E-S e todos os conhecidos em execução.</summary>
    public IReadOnlyList<BackgroundConsumer> Inspect(int top = 8)
    {
        var all = _reader.Read()
            .Where(p => !_games.Any(g => g.MatchesProcess(p.Name)))
            .Select(p => new BackgroundConsumer(p, Category(p.Name)))
            .ToList();

        var heavy = all.OrderByDescending(c => c.Usage.CpuPercent).Take(top)
            .Concat(all.OrderByDescending(c => c.Usage.PrivateMemoryMb).Take(top))
            .Concat(all.OrderByDescending(c => c.Usage.IoBytesPerSecond).Take(3))
            .Concat(all.Where(c => c.KnownCategory is not null));

        return heavy
            .GroupBy(c => c.Usage.ProcessId)
            .Select(g => g.First())
            .OrderByDescending(c => c.Usage.CpuPercent)
            .ThenByDescending(c => c.Usage.PrivateMemoryMb)
            .ToList();
    }

    private string? Category(string name)
    {
        // Instâncias do WMI vêm como "chrome#3".
        var baseName = name.Split('#')[0];
        return _known.TryGetValue(baseName, out var c) ? c : null;
    }
}
