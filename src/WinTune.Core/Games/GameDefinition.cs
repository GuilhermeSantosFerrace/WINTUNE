using WinTune.Core.Domain;

namespace WinTune.Core.Games;

/// <summary>Definição de um jogo-alvo (editável via <c>games.json</c> a partir da Fase 12).</summary>
public sealed record GameDefinition
{
    /// <summary>Jogo.</summary>
    public required GameTarget Target { get; init; }

    /// <summary>Nome de exibição.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Nomes de processo a detectar (sem caminho; com ou sem <c>.exe</c>).</summary>
    public required IReadOnlyList<string> ProcessNames { get; init; }

    /// <summary>App ID da Steam (0 = não disponível na Steam).</summary>
    public int SteamAppId { get; init; }

    /// <summary>Caminho do executável relativo à pasta de instalação (usado para confirmar a instalação).</summary>
    public required string ExecutableRelativePath { get; init; }

    /// <summary>Anti-cheat (informativo).</summary>
    public string AntiCheat { get; init; } = string.Empty;

    /// <summary>Definições padrão (seção 4.3 e matriz de verificação, seção 11).</summary>
    public static IReadOnlyList<GameDefinition> Defaults { get; } =
    [
        new GameDefinition
        {
            Target = GameTarget.RainbowSixSiege,
            DisplayName = "Rainbow Six Siege",
            // V2: RainbowSix.exe confirmado. RainbowSix_Vulkan.exe é legado (V3) e RainbowSix_BE.exe não foi confirmado (V4).
            ProcessNames = ["RainbowSix.exe"],
            SteamAppId = 359550,
            ExecutableRelativePath = "RainbowSix.exe",
            AntiCheat = "BattlEye",
        },
        new GameDefinition
        {
            Target = GameTarget.Cs2,
            DisplayName = "Counter-Strike 2",
            ProcessNames = ["cs2.exe"],
            SteamAppId = 730,
            ExecutableRelativePath = @"game\bin\win64\cs2.exe",
            AntiCheat = "VAC",
        },
    ];

    /// <summary>Indica se o nome de processo (sem .exe) corresponde a este jogo.</summary>
    public bool MatchesProcess(string processName)
    {
        var name = StripExe(processName);
        return ProcessNames.Any(p => string.Equals(StripExe(p), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string StripExe(string n) =>
        n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
}
