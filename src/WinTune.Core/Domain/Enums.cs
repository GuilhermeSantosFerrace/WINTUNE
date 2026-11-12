namespace WinTune.Core.Domain;

/// <summary>Foco de uso escolhido pelo usuário.</summary>
public enum Focus
{
    /// <summary>Trabalho / produtividade.</summary>
    Work,

    /// <summary>Jogos.</summary>
    Gaming,

    /// <summary>Os dois (Trabalho e Jogos).</summary>
    Both,

    /// <summary>Jogos e livestreaming: tudo do foco Jogos mais verificações e guias de transmissão.</summary>
    Streaming,
}

/// <summary>Cenários com problema marcados pelo usuário.</summary>
[Flags]
public enum Scenario
{
    /// <summary>Nenhum cenário (ajuste vale para qualquer cenário).</summary>
    None = 0,

    /// <summary>Travadas (stutter).</summary>
    Stutter = 1,

    /// <summary>FPS baixo.</summary>
    LowFps = 2,

    /// <summary>Lentidão com muitos apps.</summary>
    SlowMultitasking = 4,

    /// <summary>Boot lento.</summary>
    SlowBoot = 8,

    /// <summary>Temperatura alta.</summary>
    HighTemperature = 16,

    /// <summary>Carregamentos longos.</summary>
    LongLoading = 32,

    /// <summary>Quedas de quadros ou travadas na transmissão ao vivo (só vale no foco Jogos + live).</summary>
    StreamDrops = 64,
}

/// <summary>Jogos-alvo (seção 4.3).</summary>
[Flags]
public enum GameTarget
{
    /// <summary>Nenhum jogo específico.</summary>
    None = 0,

    /// <summary>Rainbow Six Siege.</summary>
    RainbowSixSiege = 1,

    /// <summary>Counter-Strike 2.</summary>
    Cs2 = 2,
}

/// <summary>Risco de um ajuste.</summary>
public enum Risk
{
    /// <summary>Seguro.</summary>
    Safe,

    /// <summary>Muda a aparência.</summary>
    Cosmetic,

    /// <summary>Exige reinício para valer.</summary>
    NeedsReboot,
}

/// <summary>Gravidade de um achado do diagnóstico.</summary>
public enum Severity
{
    /// <summary>Informativo.</summary>
    Info,

    /// <summary>Atenção.</summary>
    Warning,

    /// <summary>Impacto alto.</summary>
    Critical,

    /// <summary>A regra não pôde ser avaliada.</summary>
    Error,
}

/// <summary>Modo de reversão.</summary>
public enum RevertMode
{
    /// <summary>Não sobrescreve valores alterados depois por terceiros (retorna conflito).</summary>
    Safe,

    /// <summary>Restaura o original mesmo havendo drift (após confirmação do usuário).</summary>
    Force,
}

/// <summary>Extensões de domínio.</summary>
public static class DomainExtensions
{
    /// <summary>Indica se o foco inclui jogos.</summary>
    public static bool IncludesGaming(this Focus focus) => focus is Focus.Gaming or Focus.Both or Focus.Streaming;

    /// <summary>Indica se o foco inclui livestreaming.</summary>
    public static bool IncludesStreaming(this Focus focus) => focus == Focus.Streaming;

    /// <summary>
    /// Indica se um ajuste declarado para <paramref name="tweakFoci"/> vale neste foco. "Os dois" vale para tudo;
    /// "Jogos + live" vale para ajustes de Jogos e de Streaming.
    /// </summary>
    public static bool Matches(this Focus focus, IReadOnlyCollection<Focus> tweakFoci)
    {
        ArgumentNullException.ThrowIfNull(tweakFoci);
        return focus switch
        {
            Focus.Both => true,
            Focus.Streaming => tweakFoci.Contains(Focus.Gaming) || tweakFoci.Contains(Focus.Streaming),
            _ => tweakFoci.Contains(focus),
        };
    }

    /// <summary>Nome curto do jogo usado em rótulos de sessão (<c>siege</c>, <c>cs2</c>).</summary>
    public static string ToLabel(this GameTarget game) => game switch
    {
        GameTarget.RainbowSixSiege => "siege",
        GameTarget.Cs2 => "cs2",
        GameTarget.None => "none",
        _ => "multi",
    };

    /// <summary>Nome de exibição pt-BR.</summary>
    public static string DisplayName(this GameTarget game) => game switch
    {
        GameTarget.RainbowSixSiege => "Rainbow Six Siege",
        GameTarget.Cs2 => "Counter-Strike 2",
        GameTarget.None => "Nenhum",
        _ => "Rainbow Six Siege e Counter-Strike 2",
    };
}
