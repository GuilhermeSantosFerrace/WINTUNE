using WinTune.Core.Backup;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Tweaks;

/// <summary>Um valor de registro controlado por um <see cref="RegistryTweak"/>.</summary>
public sealed class RegistrySetting
{
    /// <summary>Cria a definição.</summary>
    public RegistrySetting(
        RegistryHive hive,
        string keyPath,
        string name,
        Func<RegistryValue?, RegistryValue> desired,
        Func<RegistryValue?, bool> isSatisfied,
        Func<RegistryValue?, RegistryValue?, RegistryValue?>? mergeRestore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        Hive = hive;
        KeyPath = keyPath;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Desired = desired;
        IsSatisfied = isSatisfied;
        MergeRestore = mergeRestore;
    }

    /// <summary>Hive.</summary>
    public RegistryHive Hive { get; }

    /// <summary>Caminho da chave.</summary>
    public string KeyPath { get; }

    /// <summary>Nome do valor.</summary>
    public string Name { get; }

    /// <summary>Calcula o valor desejado a partir do atual (permite mesclar strings com flags).</summary>
    public Func<RegistryValue?, RegistryValue> Desired { get; }

    /// <summary>Indica se o valor atual já atende ao desejado.</summary>
    public Func<RegistryValue?, bool> IsSatisfied { get; }

    /// <summary>
    /// Opcional: calcula o valor a restaurar (null = apagar) a partir do atual e do original, preservando mudanças
    /// de terceiros (ex.: outras flags numa string). Quando definido, não há verificação de drift.
    /// </summary>
    public Func<RegistryValue?, RegistryValue?, RegistryValue?>? MergeRestore { get; }

    /// <summary>Caminho legível (ex.: <c>HKCU\Software\...\Nome</c>).</summary>
    public string DisplayPath => $"{(Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{KeyPath}\\{Name}";

    /// <summary>Atalho para valor fixo.</summary>
    public static RegistrySetting Fixed(RegistryHive hive, string keyPath, string name, RegistryValue value) =>
        new(hive, keyPath, name, _ => value, current => value.Equals(current));

    /// <summary>Atalho para DWORD fixo.</summary>
    public static RegistrySetting DWord(RegistryHive hive, string keyPath, string name, int value) =>
        Fixed(hive, keyPath, name, RegistryValue.FromDWord(value));
}

/// <summary>Metadados de um ajuste (preenchidos pelo catálogo).</summary>
public sealed record TweakInfo
{
    /// <summary>ID.</summary>
    public required string Id { get; init; }

    /// <summary>Nome.</summary>
    public required string Name { get; init; }

    /// <summary>Descrição pt-BR.</summary>
    public required string Description { get; init; }

    /// <summary>Focos.</summary>
    public required Focus[] For { get; init; }

    /// <summary>Cenários.</summary>
    public Scenario Helps { get; init; } = Scenario.None;

    /// <summary>Risco.</summary>
    public Risk Risk { get; init; } = Risk.Safe;

    /// <summary>Jogos.</summary>
    public GameTarget Games { get; init; } = GameTarget.None;

    /// <summary>Prioridade estática.</summary>
    public int Priority { get; init; } = 50;

    /// <summary>Marcado por padrão.</summary>
    public bool DefaultSelected { get; init; } = true;
}

/// <summary>
/// Ajuste baseado em valores de registro (DWORD/String), com vários valores, rollback parcial automático e
/// reversão segura contra drift.
/// </summary>
public class RegistryTweak : Tweak
{
    private readonly IRegistry _registry;
    private readonly TweakInfo _info;

    /// <summary>Cria o ajuste.</summary>
    public RegistryTweak(IRegistry registry, TweakInfo info, IReadOnlyList<RegistrySetting> settings)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        if (settings is null || settings.Count == 0)
        {
            throw new ArgumentException("Pelo menos um valor de registro é necessário.", nameof(settings));
        }

        Settings = settings;
    }

    /// <summary>Valores controlados (allowlist em código para a reversão).</summary>
    public IReadOnlyList<RegistrySetting> Settings { get; }

    /// <inheritdoc />
    public override string Id => _info.Id;

    /// <inheritdoc />
    public override string Name => _info.Name;

    /// <inheritdoc />
    public override string Description => _info.Description;

    /// <inheritdoc />
    public override Focus[] For => _info.For;

    /// <inheritdoc />
    public override Scenario Helps => _info.Helps;

    /// <inheritdoc />
    public override Risk Risk => _info.Risk;

    /// <inheritdoc />
    public override GameTarget Games => _info.Games;

    /// <inheritdoc />
    public override int Priority => _info.Priority;

    /// <inheritdoc />
    public override bool DefaultSelected => _info.DefaultSelected;

    /// <inheritdoc />
    public override string Mechanism => string.Join("; ", Settings.Select(s => s.DisplayPath));

    /// <summary>Registro usado (para subclasses).</summary>
    protected IRegistry Registry => _registry;

    /// <inheritdoc />
    public override bool IsApplied() => Settings.All(s => s.IsSatisfied(Read(s)));

    /// <inheritdoc />
    public override IReadOnlyList<string> DescribeChanges() => Settings.Select(s =>
    {
        var current = Read(s);
        return s.IsSatisfied(current)
            ? $"{s.DisplayPath}: já está em {Format(current)} (sem mudança)"
            : $"{s.DisplayPath}: {Format(current)} → {Format(s.Desired(current))}";
    }).ToList();

    /// <inheritdoc />
    public override TweakResult Apply(BackupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (IsApplied())
        {
            return TweakResult.Skipped(Id, "Já aplicado; nada a fazer.");
        }

        List<RegistryValue?> currents;
        List<RegistryValue> desired;
        try
        {
            currents = Settings.Select(Read).ToList();
            desired = Settings.Select((s, i) => s.Desired(currents[i])).ToList();
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            store.Journal("apply", Id, "failed", ex.Message);
            return TweakResult.Failed(Id, ex.Message);
        }

        var opId = store.BeginOperation(Id, "apply");
        var entries = Settings.Select((s, i) => new RegistryBackupEntry
        {
            Hive = s.Hive,
            KeyPath = s.KeyPath,
            Name = s.Name,
            Original = StoredRegistryValue.From(currents[i]),
            Applied = StoredRegistryValue.From(desired[i]),
        }).ToList();

        // O original é gravado (e persistido) ANTES de qualquer escrita; nunca é sobrescrito.
        var createdBackup = store.TryAddOriginal(new TweakBackup { TweakId = Id, Registry = entries });
        if (!createdBackup)
        {
            store.MergeRegistry(Id, entries);
        }

        var written = new List<int>();
        for (var i = 0; i < Settings.Count; i++)
        {
            var s = Settings[i];
            if (s.IsSatisfied(currents[i]))
            {
                continue;
            }

            try
            {
                _registry.SetValue(s.Hive, s.KeyPath, s.Name, desired[i]);
                written.Add(i);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                var rollbackError = Rollback(written, currents);
                if (createdBackup && rollbackError is null)
                {
                    store.Remove(Id);
                }

                store.CompleteOperation(opId);
                var msg = $"Falha ao gravar {s.DisplayPath}: {ex.Message}." +
                          (rollbackError is null ? " Valores já gravados foram revertidos." : $" Rollback incompleto: {rollbackError}");
                store.Journal("apply", Id, "failed", msg);
                return TweakResult.Failed(Id, msg);
            }
        }

        store.CompleteOperation(opId);
        store.Journal("apply", Id, "success");
        return TweakResult.Success(Id, RequiresReboot ? "Aplicado. Reinicie para valer." : "Aplicado.", RequiresReboot);
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

        // Allowlist: entradas do arquivo que não correspondem a valores declarados em código são ignoradas.
        foreach (var stray in backup.Registry.Where(e => !Settings.Any(s => e.Matches(s.Hive, s.KeyPath, s.Name))))
        {
            store.Journal("revert", Id, "ignored-outside-allowlist", $"{stray.Hive}\\{stray.KeyPath}\\{stray.Name}");
        }

        var plan = new List<(RegistrySetting Setting, RegistryValue? Target)>();
        var conflicts = new List<string>();
        foreach (var s in Settings)
        {
            var entry = backup.Registry.FirstOrDefault(e => e.Matches(s.Hive, s.KeyPath, s.Name));
            if (entry is null)
            {
                continue;
            }

            RegistryValue? original, applied;
            try
            {
                original = entry.Original?.ToValue();
                applied = entry.Applied?.ToValue();
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException)
            {
                store.Journal("revert", Id, "invalid-entry", s.DisplayPath);
                continue;
            }

            var current = Read(s);
            if (s.MergeRestore is not null)
            {
                var target = s.MergeRestore(current, original);
                if (!Equals(current, target))
                {
                    plan.Add((s, target));
                }

                continue;
            }

            if (Equals(current, original))
            {
                continue;
            }

            if (Equals(current, applied) || mode == RevertMode.Force)
            {
                plan.Add((s, original));
            }
            else
            {
                conflicts.Add($"{s.DisplayPath}: atual {Format(current)}, aplicado pelo WinTune {Format(applied)}");
            }
        }

        if (conflicts.Count > 0 && mode == RevertMode.Safe)
        {
            var msg = "Outro programa ou o usuário alterou estes valores depois do WinTune; nada foi sobrescrito: " +
                      string.Join("; ", conflicts);
            store.Journal("revert", Id, "conflict", msg);
            return TweakResult.Conflict(Id, msg);
        }

        var opId = store.BeginOperation(Id, "revert");
        foreach (var (s, target) in plan)
        {
            try
            {
                WriteOrDelete(s, target);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                store.CompleteOperation(opId);
                var msg = $"Falha ao restaurar {s.DisplayPath}: {ex.Message}. O backup foi mantido para nova tentativa.";
                store.Journal("revert", Id, "failed", msg);
                return TweakResult.Failed(Id, msg);
            }
        }

        store.Remove(Id);
        store.CompleteOperation(opId);
        store.Journal("revert", Id, "success", mode == RevertMode.Force ? "forçado" : null);
        return TweakResult.Success(Id, RequiresReboot ? "Revertido. Reinicie para valer." : "Revertido.", RequiresReboot);
    }

    /// <summary>Formata um valor para exibição.</summary>
    protected static string Format(RegistryValue? v) => v?.ToString() ?? "(inexistente)";

    private RegistryValue? Read(RegistrySetting s) => _registry.GetValue(s.Hive, s.KeyPath, s.Name);

    private void WriteOrDelete(RegistrySetting s, RegistryValue? value)
    {
        if (value is null)
        {
            _registry.DeleteValue(s.Hive, s.KeyPath, s.Name);
        }
        else
        {
            _registry.SetValue(s.Hive, s.KeyPath, s.Name, value);
        }
    }

    private string? Rollback(List<int> written, List<RegistryValue?> originals)
    {
        var errors = new List<string>();
        foreach (var i in Enumerable.Reverse(written))
        {
            try
            {
                WriteOrDelete(Settings[i], originals[i]);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                errors.Add($"{Settings[i].DisplayPath}: {ex.Message}");
            }
        }

        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or InvalidOperationException or NotSupportedException;
}
