using WinTune.Core.Backup;
using WinTune.Core.Domain;

namespace WinTune.Core.Tweaks;

/// <summary>
/// Contrato central de um ajuste reversível (seção 3.3).
/// <list type="bullet">
/// <item><c>Apply</c> grava o original no <see cref="BackupStore"/> antes de alterar e nunca sobrescreve um backup existente.</item>
/// <item><c>Revert</c> restaura o original (apaga o valor se ele não existia) e não sobrescreve mudanças de terceiros no modo seguro.</item>
/// <item><c>IsApplied</c> lê o estado real, sem cache.</item>
/// </list>
/// </summary>
public abstract class Tweak
{
    /// <summary>ID estável em kebab-case.</summary>
    public abstract string Id { get; }

    /// <summary>Nome de exibição.</summary>
    public abstract string Name { get; }

    /// <summary>Explicação pt-BR (o que muda e por quê).</summary>
    public abstract string Description { get; }

    /// <summary>Focos aos quais o ajuste se aplica.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Nome definido pelo contrato da seção 3.3 do spec.")]
    public abstract Focus[] For { get; }

    /// <summary>Cenários que o ajuste ajuda (None = qualquer cenário).</summary>
    public virtual Scenario Helps => Scenario.None;

    /// <summary>Risco.</summary>
    public virtual Risk Risk => Risk.Safe;

    /// <summary>Jogos específicos (None = ajuste geral).</summary>
    public virtual GameTarget Games => GameTarget.None;

    /// <summary>Onde muda (chave de registro / comando), para transparência.</summary>
    public abstract string Mechanism { get; }

    /// <summary>Prioridade estática (maior = mais relevante), usada na ordenação. Ver docs/TWEAKS.md.</summary>
    public virtual int Priority => 50;

    /// <summary>Se o ajuste vem marcado por padrão.</summary>
    public virtual bool DefaultSelected => true;

    /// <summary>Indica se exige reinício.</summary>
    public bool RequiresReboot => Risk == Risk.NeedsReboot;

    /// <summary>Conselho para o hardware detectado (padrão: aplicável, com <see cref="DefaultSelected"/>).</summary>
    public virtual TweakAdvice GetAdvice(SystemProfile profile) => new(true, DefaultSelected);

    /// <summary>Lê o estado real e indica se o valor desejado já está ativo.</summary>
    public abstract bool IsApplied();

    /// <summary>Descreve (sem alterar nada) o que seria feito: modo simulação.</summary>
    public abstract IReadOnlyList<string> DescribeChanges();

    /// <summary>Aplica o ajuste.</summary>
    public abstract TweakResult Apply(BackupStore store);

    /// <summary>Indica se há backup para reverter.</summary>
    public abstract bool CanRevert(BackupStore store);

    /// <summary>Reverte no modo seguro (sem sobrescrever drift).</summary>
    public TweakResult Revert(BackupStore store) => Revert(store, RevertMode.Safe);

    /// <summary>Reverte no modo indicado.</summary>
    public abstract TweakResult Revert(BackupStore store, RevertMode mode);
}
