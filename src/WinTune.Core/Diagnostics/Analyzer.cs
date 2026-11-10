using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Diagnostics;

/// <summary>Dependências somente leitura disponíveis para as regras.</summary>
/// <param name="Wmi">WMI.</param>
/// <param name="Registry">Registro (as regras nunca escrevem).</param>
/// <param name="FileSystem">Sistema de arquivos.</param>
/// <param name="Environment">Ambiente.</param>
/// <param name="Clock">Relógio.</param>
/// <param name="Profile">Perfil de hardware já detectado.</param>
public sealed record DiagnosticContext(
    IWmiReader Wmi,
    IRegistry Registry,
    IFileSystem FileSystem,
    IEnvironmentInfo Environment,
    IClock Clock,
    SystemProfile Profile);

/// <summary>Uma regra de diagnóstico (somente leitura, nunca altera o sistema).</summary>
public interface IDiagnosticRule
{
    /// <summary>ID do achado que a regra produz.</summary>
    string Id { get; }

    /// <summary>Avalia a regra.</summary>
    IEnumerable<Finding> Evaluate(DiagnosticContext context);
}

/// <summary>Executa as regras isoladamente: a falha de uma não derruba as outras.</summary>
public sealed class Analyzer
{
    private readonly IReadOnlyList<IDiagnosticRule> _rules;

    /// <summary>Cria o analisador com as regras informadas.</summary>
    public Analyzer(IEnumerable<IDiagnosticRule> rules)
    {
        _rules = (rules ?? throw new ArgumentNullException(nameof(rules))).ToList();
    }

    /// <summary>Regras do diagnóstico estático (seção 4.2, Fase 5).</summary>
    public static IReadOnlyList<IDiagnosticRule> StaticRules() =>
    [
        new RamBelowRatedRule(),
        new RamSingleChannelRule(),
        new RamCapacityLowRule(),
        new RebarUnknownRule(),
        new VbsActiveRule(),
        new ChipsetDriverMissingRule(),
        new FastStartupRule(),
        new DiskLowSpaceRule(),
        new VCacheNoteRule(),
        new GpuDriverOldRule(),
    ];

    /// <summary>Regras cadastradas.</summary>
    public IReadOnlyList<IDiagnosticRule> Rules => _rules;

    /// <summary>Avalia todas as regras.</summary>
    public IReadOnlyList<Finding> Run(DiagnosticContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var findings = new List<Finding>();
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                findings.AddRange(rule.Evaluate(context).ToList());
            }
#pragma warning disable CA1031 // Isolamento proposital: qualquer falha vira um achado informativo.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                findings.Add(new Finding(rule.Id, "Não foi possível avaliar esta verificação",
                    $"A verificação \"{rule.Id}\" falhou e foi ignorada: {ex.Message}", Severity.Error));
            }
        }

        return findings;
    }
}
