using System.Text.RegularExpressions;
using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Tweaks;

/// <summary>Plano de energia listado pelo <c>powercfg</c>.</summary>
/// <param name="Guid">GUID (minúsculo).</param>
/// <param name="Name">Nome localizado.</param>
/// <param name="Active">Indica o plano ativo.</param>
public sealed record PowerScheme(string Guid, string Name, bool Active);

/// <summary>Leitura e parsing do <c>powercfg</c>, independentes do idioma (regex de GUID).</summary>
public static partial class PowerCfg
{
    /// <summary>Executável (System32).</summary>
    public const string Exe = "powercfg.exe";

    /// <summary>GUID do plano "Alto desempenho" do Windows.</summary>
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Extrai o primeiro GUID da saída (ex.: <c>/getactivescheme</c>, <c>/duplicatescheme</c>).</summary>
    public static string? ParseFirstGuid(string output)
    {
        var m = GuidRegex().Match(output ?? string.Empty);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>Faz o parsing de <c>powercfg /list</c> em qualquer idioma.</summary>
    public static IReadOnlyList<PowerScheme> ParseList(string output)
    {
        var list = new List<PowerScheme>();
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            var m = SchemeLineRegex().Match(line);
            if (m.Success)
            {
                list.Add(new PowerScheme(m.Groups["guid"].Value.ToLowerInvariant(), m.Groups["name"].Value.Trim(), m.Groups["active"].Success));
            }
        }

        return list;
    }

    /// <summary>Valida o formato de um GUID.</summary>
    public static bool IsGuid(string? value) => value is not null && FullGuidRegex().IsMatch(value);

    /// <summary>Indica se o plano parece ser o "AMD Ryzen Balanced" do driver de chipset.</summary>
    public static bool IsAmdRyzenBalanced(PowerScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        return scheme.Name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Plano ativo (GUID) ou null.</summary>
    public static string? GetActive(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var r = runner.Run(Exe, ["/getactivescheme"], Timeout);
        return r.Succeeded ? ParseFirstGuid(r.StandardOutput) : null;
    }

    /// <summary>Lista os planos (vazio em caso de falha).</summary>
    public static IReadOnlyList<PowerScheme> List(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        var r = runner.Run(Exe, ["/list"], Timeout);
        return r.Succeeded ? ParseList(r.StandardOutput) : [];
    }

    /// <summary>Executa um subcomando com GUID validado.</summary>
    internal static ProcessResult RunWithGuid(IProcessRunner runner, string verb, string guid)
    {
        if (!IsGuid(guid))
        {
            throw new ArgumentException("GUID inválido.", nameof(guid));
        }

        return runner.Run(Exe, [verb, guid], Timeout);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex FullGuidRegex();

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((?<name>[^)]*)\)\s*(?<active>\*)?\s*$")]
    private static partial Regex SchemeLineRegex();
}

/// <summary>
/// Ativa o plano "Alto desempenho" (com fallback <c>/duplicatescheme</c>) e reverte para o GUID anterior exato.
/// </summary>
public sealed class PowerPlanTweak : Tweak
{
    internal const string PreviousKey = "previousScheme";
    internal const string AppliedKey = "appliedScheme";
    internal const string CreatedKey = "createdScheme";

    private readonly IProcessRunner _runner;

    /// <summary>Cria o ajuste.</summary>
    public PowerPlanTweak(IProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc />
    public override string Id => "power-high";

    /// <inheritdoc />
    public override string Name => "Plano de energia Alto desempenho";

    /// <inheritdoc />
    public override string Description =>
        "Ativa o plano Alto desempenho, que mantém CPU e barramentos em estados de energia mais altos e reduz a latência para " +
        "subir o clock. Aumenta o consumo em repouso. O plano anterior é guardado e restaurado exatamente ao desfazer.";

    /// <inheritdoc />
    public override Focus[] For => [Focus.Gaming];

    /// <inheritdoc />
    public override Scenario Helps => Scenario.Stutter | Scenario.LowFps;

    /// <inheritdoc />
    public override int Priority => 55;

    /// <inheritdoc />
    public override string Mechanism => $"powercfg /setactive {PowerCfg.HighPerformanceGuid} (fallback: powercfg /duplicatescheme)";

    /// <inheritdoc />
    public override TweakAdvice GetAdvice(SystemProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.HasAmdRyzenBalancedPlan
            ? new TweakAdvice(true, false,
                "O plano \"AMD Ryzen Balanced\" do driver de chipset está instalado; ele já ajusta a CPU Ryzen e o ganho do Alto desempenho tende a ser menor. Desmarcado por padrão.")
            : new TweakAdvice(true, true);
    }

    /// <inheritdoc />
    public override bool IsApplied()
    {
        var active = PowerCfg.GetActive(_runner);
        if (active is null)
        {
            return false;
        }

        if (active == PowerCfg.HighPerformanceGuid)
        {
            return true;
        }

        var scheme = PowerCfg.List(_runner).FirstOrDefault(s => s.Guid == active);
        return scheme is not null && HighPerformanceNames.Contains(scheme.Name);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> DescribeChanges()
    {
        var active = PowerCfg.GetActive(_runner) ?? "(desconhecido)";
        return [$"Plano ativo: {active} → {PowerCfg.HighPerformanceGuid} (Alto desempenho; criado por /duplicatescheme se não existir)"];
    }

    /// <inheritdoc />
    public override TweakResult Apply(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var active = PowerCfg.GetActive(_runner);
        if (active is null)
        {
            return Fail(store, "Não foi possível ler o plano de energia ativo.");
        }

        var schemes = PowerCfg.List(_runner);
        string? target = schemes.Any(s => s.Guid == PowerCfg.HighPerformanceGuid) ? PowerCfg.HighPerformanceGuid : null;
        if (active == PowerCfg.HighPerformanceGuid || (target is null && IsApplied()))
        {
            return TweakResult.Skipped(Id, "O plano Alto desempenho já está ativo; nada a fazer.");
        }

        var opId = store.BeginOperation(Id, "apply");
        var created = false;
        if (target is null)
        {
            var dup = PowerCfg.RunWithGuid(_runner, "/duplicatescheme", PowerCfg.HighPerformanceGuid);
            var newGuid = dup.Succeeded ? PowerCfg.ParseFirstGuid(dup.StandardOutput) : null;
            if (newGuid is null || newGuid == PowerCfg.HighPerformanceGuid)
            {
                store.CompleteOperation(opId);
                return Fail(store, "O plano Alto desempenho não existe e não pôde ser criado (/duplicatescheme falhou).");
            }

            target = newGuid;
            created = true;
        }

        var newBackup = store.TryAddOriginal(new TweakBackup
        {
            TweakId = Id,
            Extras = new Dictionary<string, string>(StringComparer.Ordinal) { [PreviousKey] = active },
        });
        store.SetExtra(Id, AppliedKey, target, overwrite: true);
        if (created)
        {
            store.SetExtra(Id, CreatedKey, target, overwrite: true);
        }

        var set = PowerCfg.RunWithGuid(_runner, "/setactive", target);
        if (!set.Succeeded || PowerCfg.GetActive(_runner) != target)
        {
            if (created)
            {
                PowerCfg.RunWithGuid(_runner, "/delete", target);
            }

            if (newBackup)
            {
                store.Remove(Id);
            }

            store.CompleteOperation(opId);
            return Fail(store, "powercfg /setactive falhou: " + set.StandardError.Trim());
        }

        store.CompleteOperation(opId);
        store.Journal("apply", Id, "success", $"{active} → {target}");
        return TweakResult.Success(Id, "Plano Alto desempenho ativado.");
    }

    /// <inheritdoc />
    public override bool CanRevert(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return store.HasBackup(Id);
    }

    /// <inheritdoc />
    public override TweakResult Revert(BackupStore store, RevertMode mode)
    {
        ArgumentNullException.ThrowIfNull(store);
        var backup = store.Get(Id);
        if (backup is null)
        {
            return TweakResult.Skipped(Id, "Nada para desfazer.");
        }

        backup.Extras.TryGetValue(PreviousKey, out var previous);
        backup.Extras.TryGetValue(AppliedKey, out var applied);
        backup.Extras.TryGetValue(CreatedKey, out var createdScheme);
        if (!PowerCfg.IsGuid(previous))
        {
            store.Journal("revert", Id, "invalid-entry", "GUID anterior inválido no backup");
            return TweakResult.Failed(Id, "O backup não contém um GUID de plano anterior válido.");
        }

        var schemes = PowerCfg.List(_runner);
        var current = PowerCfg.GetActive(_runner);

        // Allowlist: só restaura um GUID que o próprio powercfg reconhece.
        if (!schemes.Any(s => s.Guid == previous))
        {
            return TweakResult.Failed(Id, $"O plano anterior ({previous}) não existe mais. Escolha um plano em Opções de Energia.");
        }

        if (current != previous && current != applied && mode == RevertMode.Safe)
        {
            var msg = $"O plano ativo ({current}) foi trocado depois do WinTune; nada foi alterado.";
            store.Journal("revert", Id, "conflict", msg);
            return TweakResult.Conflict(Id, msg);
        }

        var opId = store.BeginOperation(Id, "revert");
        if (current != previous)
        {
            var set = PowerCfg.RunWithGuid(_runner, "/setactive", previous!);
            if (!set.Succeeded)
            {
                store.CompleteOperation(opId);
                return Fail(store, "powercfg /setactive falhou ao restaurar: " + set.StandardError.Trim());
            }
        }

        if (PowerCfg.IsGuid(createdScheme) && createdScheme != previous && createdScheme != PowerCfg.HighPerformanceGuid &&
            schemes.Any(s => s.Guid == createdScheme))
        {
            PowerCfg.RunWithGuid(_runner, "/delete", createdScheme!);
        }

        store.Remove(Id);
        store.CompleteOperation(opId);
        store.Journal("revert", Id, "success", $"→ {previous}");
        return TweakResult.Success(Id, "Plano de energia anterior restaurado.");
    }

    private static readonly HashSet<string> HighPerformanceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "High performance",
        "Alto desempenho",
    };

    private TweakResult Fail(BackupStore store, string message)
    {
        store.Journal("apply", Id, "failed", message);
        return TweakResult.Failed(Id, message);
    }
}
