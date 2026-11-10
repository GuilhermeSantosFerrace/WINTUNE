using WinTune.Core.Domain;
using WinTune.Core.Tweaks;

namespace WinTune.Core.Recommendations;

/// <summary>Entrada do motor de recomendação.</summary>
/// <param name="Focus">Foco escolhido.</param>
/// <param name="Scenarios">Cenários marcados.</param>
/// <param name="Games">Jogos escolhidos (ignorado se o foco for Trabalho).</param>
/// <param name="Profile">Hardware detectado.</param>
public sealed record RecommendationRequest(Focus Focus, Scenario Scenarios, GameTarget Games, SystemProfile Profile);

/// <summary>Um ajuste recomendado.</summary>
/// <param name="Tweak">Ajuste.</param>
/// <param name="Selected">Marcado por padrão.</param>
/// <param name="AlreadyApplied">O estado desejado já está ativo ("nada a fazer").</param>
/// <param name="Note">Observação (hardware, experimental, erro de leitura).</param>
public sealed record Recommendation(Tweak Tweak, bool Selected, bool AlreadyApplied, string? Note);

/// <summary>Motor de recomendação (seções 3.5 e 4.3).</summary>
public sealed class RecommendationEngine
{
    /// <summary>
    /// Regra base: <c>foco_ok &amp;&amp; cenario_ok &amp;&amp; jogo_ok</c>.
    /// </summary>
    public static bool IsRecommended(Tweak tweak, Focus focus, Scenario scenarios, GameTarget games)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        var focusOk = focus == Focus.Both || tweak.For.Contains(focus);
        var scenarioOk = scenarios == Scenario.None || tweak.Helps == Scenario.None || (tweak.Helps & scenarios) != 0;
        var gameOk = !focus.IncludesGaming() || games == GameTarget.None || tweak.Games == GameTarget.None || (tweak.Games & games) != 0;
        return focusOk && scenarioOk && gameOk;
    }

    /// <summary>Filtra, aplica o conselho por hardware e ordena por prioridade (desc) e nome.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "API de instância mantida para configuração futura (pesos, limites).")]
    public IReadOnlyList<Recommendation> Recommend(IEnumerable<Tweak> tweaks, RecommendationRequest request)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        ArgumentNullException.ThrowIfNull(request);
        var games = request.Focus.IncludesGaming() ? request.Games : GameTarget.None;
        var result = new List<Recommendation>();
        foreach (var t in tweaks.Where(t => IsRecommended(t, request.Focus, request.Scenarios, games)))
        {
            var advice = t.GetAdvice(request.Profile);
            if (!advice.Applicable)
            {
                continue;
            }

            bool applied;
            string? note = advice.Note;
            try
            {
                applied = t.IsApplied();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
            {
                applied = false;
                note = $"Não foi possível ler o estado atual: {ex.Message}";
            }

            if (applied && note is null)
            {
                note = "Já aplicado; nada a fazer aqui.";
            }

            result.Add(new Recommendation(t, advice.DefaultSelected && !applied, applied, note));
        }

        return result
            .OrderByDescending(r => r.Tweak.Priority)
            .ThenBy(r => r.Tweak.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
