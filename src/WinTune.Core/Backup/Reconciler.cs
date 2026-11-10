using WinTune.Core.Domain;
using WinTune.Core.Tweaks;

namespace WinTune.Core.Backup;

/// <summary>Resultado da reconciliação de uma operação interrompida.</summary>
/// <param name="TweakId">Ajuste.</param>
/// <param name="Action">Ação interrompida.</param>
/// <param name="Result">Resultado da tentativa de recuperação.</param>
public sealed record ReconcileOutcome(string TweakId, string Action, TweakResult Result);

/// <summary>
/// Recupera operações interrompidas (queda de energia, processo finalizado no meio de aplicar/reverter — R13).
/// Como o original é gravado antes de qualquer escrita, reverter no modo seguro restaura o que foi alterado e ignora o que
/// ainda estava no valor original.
/// </summary>
public sealed class Reconciler
{
    private readonly BackupStore _store;
    private readonly Func<string, Tweak?> _resolve;

    /// <summary>Cria o reconciliador.</summary>
    public Reconciler(BackupStore store, Func<string, Tweak?> resolve)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    /// <summary>Há operações interrompidas?</summary>
    public bool HasPending => _store.IncompleteOperations.Count > 0;

    /// <summary>Descrição das operações interrompidas.</summary>
    public IReadOnlyList<string> Describe() =>
        _store.IncompleteOperations.Select(o => $"{o.Action} de {o.TweakId} iniciado em {o.StartedUtc.ToLocalTime():dd/MM HH:mm:ss}").ToList();

    /// <summary>Desfaz o estado parcial de cada operação interrompida e encerra a intenção.</summary>
    public IReadOnlyList<ReconcileOutcome> Reconcile()
    {
        var outcomes = new List<ReconcileOutcome>();
        foreach (var op in _store.IncompleteOperations)
        {
            TweakResult result;
            var tweak = _resolve(op.TweakId);
            if (tweak is null)
            {
                result = TweakResult.Failed(op.TweakId, "Ajuste desconhecido nesta versão; veja docs/SAFETY.md para recuperação manual.");
            }
            else if (!_store.HasBackup(op.TweakId))
            {
                // Nada foi gravado antes da queda (o original é salvo antes da 1ª escrita).
                result = TweakResult.Skipped(op.TweakId, "Nada havia sido alterado.");
            }
            else
            {
                result = tweak.Revert(_store, RevertMode.Safe);
            }

            _store.CompleteOperation(op.Id);
            _store.Journal("reconcile", op.TweakId, result.Status.ToString(), $"{op.Action} interrompido: {result.Message}");
            outcomes.Add(new ReconcileOutcome(op.TweakId, op.Action, result));
        }

        return outcomes;
    }
}
