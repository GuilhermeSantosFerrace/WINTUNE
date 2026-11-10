using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Core.Profiles;

namespace WinTune.Presentation;

/// <summary>Resultado do "Desfazer tudo" sem interface (usado pelo desinstalador: <c>WinTune.exe --revert-all</c>).</summary>
/// <param name="Results">Resultado por ajuste.</param>
/// <param name="Unknown">IDs sem ajuste correspondente nesta versão.</param>
public sealed record UndoReport(IReadOnlyList<TweakResult> Results, IReadOnlyList<string> Unknown)
{
    /// <summary>Tudo foi revertido (sem conflito, falha ou ID desconhecido).</summary>
    public bool AllReverted => Unknown.Count == 0 && Results.All(r => r.Status is TweakStatus.Success or TweakStatus.Skipped);

    /// <summary>Resumo pt-BR.</summary>
    public string Summary =>
        AllReverted
            ? $"Todos os ajustes do WinTune foram desfeitos ({Results.Count})."
            : "Alguns ajustes não foram desfeitos automaticamente:\n" + string.Join("\n",
                Results.Where(r => r.Status is TweakStatus.Conflict or TweakStatus.Failed).Select(r => $"• {r.TweakId}: {r.Message}")
                    .Concat(Unknown.Select(u => $"• {u}: ajuste desconhecido"))) +
              "\n\nAbra o WinTune para resolver ou siga docs/SAFETY.md (recuperação manual).";
}

/// <summary>Reverte tudo sem perguntar; conflitos (valores mudados por terceiros) NÃO são sobrescritos.</summary>
public static class UndoService
{
    /// <summary>Reconcilia operações interrompidas, restaura o modo jogo pendente e reverte todos os backups (modo seguro).</summary>
    public static UndoReport RevertAll(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var results = new List<TweakResult>();
        var unknown = new List<string>();

        results.AddRange(new Reconciler(services.Backup, services.Resolve).Reconcile().Select(o => o.Result));

        var orchestrator = new GameModeOrchestrator(services.Processes, services.Games, services.Catalog.Find, services.Backup,
            new ProfileStore(services.FileSystem, services.Environment), services.Clock);
        orchestrator.RestorePending();

        foreach (var id in services.Backup.TweakIds)
        {
            var tweak = services.Resolve(id);
            if (tweak is null)
            {
                unknown.Add(id);
                continue;
            }

            try
            {
                results.Add(tweak.Revert(services.Backup, RevertMode.Safe));
            }
#pragma warning disable CA1031 // Um item com erro não interrompe os demais.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                results.Add(TweakResult.Failed(id, ex.Message));
            }
        }

        services.Backup.Journal("revert-all", "-", unknown.Count == 0 && results.All(r => r.Status is TweakStatus.Success or TweakStatus.Skipped) ? "success" : "partial");
        return new UndoReport(results, unknown);
    }
}
