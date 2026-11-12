namespace WinTune.Core.Domain;

/// <summary>Categoria do achado (aba onde aparece).</summary>
public enum FindingCategory
{
    /// <summary>Diagnóstico geral do sistema.</summary>
    System,

    /// <summary>Verificação ou guia específico de jogo (aba Jogos).</summary>
    Game,

    /// <summary>Verificação ou guia de livestreaming (aba Live).</summary>
    Streaming,
}

/// <summary>Achado do diagnóstico (somente aviso; nunca altera o sistema).</summary>
/// <param name="Id">Identificador estável (kebab-case).</param>
/// <param name="Title">Título curto.</param>
/// <param name="Message">Explicação e recomendação.</param>
/// <param name="Severity">Gravidade.</param>
/// <param name="Evidence">Valor medido que justifica o achado.</param>
/// <param name="Games">Jogos aos quais se aplica.</param>
/// <param name="Category">Categoria.</param>
public sealed record Finding(
    string Id,
    string Title,
    string Message,
    Severity Severity,
    string? Evidence = null,
    GameTarget Games = GameTarget.None,
    FindingCategory Category = FindingCategory.System);

/// <summary>Status do resultado de um ajuste.</summary>
public enum TweakStatus
{
    /// <summary>Concluído.</summary>
    Success,

    /// <summary>Falhou (estado anterior preservado quando possível).</summary>
    Failed,

    /// <summary>Ignorado (nada a fazer).</summary>
    Skipped,

    /// <summary>O valor atual foi alterado por terceiros; nada foi sobrescrito.</summary>
    Conflict,
}

/// <summary>Resultado de aplicar/reverter um ajuste.</summary>
/// <param name="TweakId">ID do ajuste.</param>
/// <param name="Status">Status.</param>
/// <param name="Message">Mensagem pt-BR.</param>
/// <param name="RequiresReboot">Indica que é preciso reiniciar.</param>
public sealed record TweakResult(string TweakId, TweakStatus Status, string Message, bool RequiresReboot = false)
{
    /// <summary>Cria um resultado de sucesso.</summary>
    public static TweakResult Success(string id, string message, bool requiresReboot = false) => new(id, TweakStatus.Success, message, requiresReboot);

    /// <summary>Cria um resultado de falha.</summary>
    public static TweakResult Failed(string id, string message) => new(id, TweakStatus.Failed, message);

    /// <summary>Cria um resultado ignorado.</summary>
    public static TweakResult Skipped(string id, string message) => new(id, TweakStatus.Skipped, message);

    /// <summary>Cria um resultado de conflito (drift).</summary>
    public static TweakResult Conflict(string id, string message) => new(id, TweakStatus.Conflict, message);
}

/// <summary>Hardware/sistema detectado, usado para parametrizar recomendações.</summary>
public sealed record SystemProfile
{
    /// <summary>Nome da CPU.</summary>
    public string CpuName { get; init; } = string.Empty;

    /// <summary>Fabricante da CPU (ex.: AuthenticAMD).</summary>
    public string CpuVendor { get; init; } = string.Empty;

    /// <summary>Nomes das GPUs.</summary>
    public IReadOnlyList<string> GpuNames { get; init; } = [];

    /// <summary>RAM total em bytes.</summary>
    public ulong TotalRamBytes { get; init; }

    /// <summary>O plano "AMD Ryzen Balanced" está instalado.</summary>
    public bool HasAmdRyzenBalancedPlan { get; init; }

    /// <summary>Build do Windows.</summary>
    public int OsBuild { get; init; }

    /// <summary>Há GPU NVIDIA.</summary>
    public bool HasNvidiaGpu => GpuNames.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));

    /// <summary>Quantidade de GPUs.</summary>
    public int GpuCount => GpuNames.Count;

    /// <summary>Perfil vazio (nada detectado).</summary>
    public static SystemProfile Unknown { get; } = new();
}

/// <summary>Conselho do ajuste para um perfil de sistema.</summary>
/// <param name="Applicable">Se o ajuste faz sentido neste hardware.</param>
/// <param name="DefaultSelected">Se vem marcado por padrão.</param>
/// <param name="Note">Observação exibida ao usuário.</param>
public sealed record TweakAdvice(bool Applicable, bool DefaultSelected, string? Note = null);
