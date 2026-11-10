using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Backup;

/// <summary>
/// Armazena os valores originais de cada ajuste, o histórico (journal) e as intenções de operação.
/// Escrita atômica, schema versionado, tolerante a arquivo corrompido.
/// </summary>
/// <remarks>
/// Anti-adulteração: o conteúdo do arquivo NUNCA define o que é escrito no registro. Cada ajuste só restaura
/// entradas que pertençam à sua própria lista declarada em código (allowlist).
/// </remarks>
public sealed partial class BackupStore
{
    /// <summary>Tamanho máximo aceito do arquivo de backup.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Máximo de entradas no histórico (as mais antigas são descartadas).</summary>
    public const int MaxJournalEntries = 5000;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly BackupDocument _doc;

    private BackupStore(IFileSystem fs, IClock clock, string path, BackupDocument doc, string? warning)
    {
        _fs = fs;
        _clock = clock;
        FilePath = path;
        _doc = doc;
        LoadWarning = warning;
    }

    /// <summary>Caminho do arquivo.</summary>
    public string FilePath { get; }

    /// <summary>Aviso de carregamento (ex.: arquivo corrompido movido para <c>.corrupt</c>).</summary>
    public string? LoadWarning { get; }

    /// <summary>Caminho padrão: <c>%ProgramData%\WinTune\backup.json</c> (pasta com escrita só para Administradores).</summary>
    public static string DefaultPath(IEnvironmentInfo env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return Path.Combine(env.GetFolder(KnownFolder.ProgramData), "WinTune", "backup.json");
    }

    /// <summary>Abre (ou cria) o backup. Arquivo inválido é movido para <c>.corrupt-*</c> e um aviso é exposto.</summary>
    public static BackupStore Open(IFileSystem fs, IClock clock, string path)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(clock);
        if (!fs.FileExists(path))
        {
            return new BackupStore(fs, clock, path, new BackupDocument(), null);
        }

        try
        {
            var entry = fs.GetFile(path);
            if (entry is not null && entry.Length > MaxFileBytes)
            {
                throw new InvalidDataException("Arquivo de backup excede o tamanho máximo.");
            }

            var doc = JsonSerializer.Deserialize<BackupDocument>(fs.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Backup vazio.");
            Validate(doc);
            return new BackupStore(fs, clock, path, doc, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or FormatException)
        {
            var corrupt = $"{path}.corrupt-{clock.UtcNow:yyyyMMddHHmmss}";
            fs.MoveFile(path, corrupt, overwrite: true);
            var store = new BackupStore(fs, clock, path, new BackupDocument(),
                $"O arquivo de backup estava inválido e foi movido para \"{corrupt}\". Ajustes aplicados antes disso não podem ser desfeitos automaticamente; veja docs/SAFETY.md para a recuperação manual.");
            store.Journal("backup-load", "-", "corrupt", ex.Message);
            return store;
        }
    }

    /// <summary>IDs com backup.</summary>
    public IReadOnlyList<string> TweakIds
    {
        get
        {
            lock (_gate)
            {
                return _doc.Entries.Keys.ToList();
            }
        }
    }

    /// <summary>Histórico (cópia).</summary>
    public IReadOnlyList<JournalEntry> JournalEntries
    {
        get
        {
            lock (_gate)
            {
                return _doc.Journal.ToList();
            }
        }
    }

    /// <summary>Operações iniciadas e não concluídas (queda no meio de aplicar/reverter).</summary>
    public IReadOnlyList<OperationRecord> IncompleteOperations
    {
        get
        {
            lock (_gate)
            {
                return _doc.Operations.Where(o => o.CompletedUtc is null).ToList();
            }
        }
    }

    /// <summary>Indica se há backup do ajuste.</summary>
    public bool HasBackup(string tweakId)
    {
        lock (_gate)
        {
            return _doc.Entries.ContainsKey(tweakId);
        }
    }

    /// <summary>Cópia do backup do ajuste (ou null).</summary>
    public TweakBackup? Get(string tweakId)
    {
        lock (_gate)
        {
            return _doc.Entries.TryGetValue(tweakId, out var b) ? Clone(b) : null;
        }
    }

    /// <summary>Grava o backup original. Retorna falso (sem alterar nada) se já existir: o original nunca é sobrescrito.</summary>
    public bool TryAddOriginal(TweakBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ValidateId(backup.TweakId);
        lock (_gate)
        {
            if (_doc.Entries.ContainsKey(backup.TweakId))
            {
                return false;
            }

            var copy = Clone(backup);
            copy.CreatedUtc = _clock.UtcNow;
            _doc.Entries[backup.TweakId] = copy;
            Save();
            return true;
        }
    }

    /// <summary>
    /// Adiciona entradas de registro que ainda não existem no backup e atualiza o valor aplicado das existentes,
    /// preservando sempre o original já guardado.
    /// </summary>
    public void MergeRegistry(string tweakId, IEnumerable<RegistryBackupEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            if (!_doc.Entries.TryGetValue(tweakId, out var backup))
            {
                throw new InvalidOperationException("Backup inexistente.");
            }

            foreach (var e in entries)
            {
                var existing = backup.Registry.FirstOrDefault(r => r.Matches(e.Hive, e.KeyPath, e.Name));
                if (existing is null)
                {
                    backup.Registry.Add(e);
                }
                else
                {
                    existing.Applied = e.Applied;
                }
            }

            Save();
        }
    }

    /// <summary>Grava um extra; por padrão não sobrescreve (preserva o original).</summary>
    public void SetExtra(string tweakId, string key, string value, bool overwrite = false)
    {
        lock (_gate)
        {
            if (!_doc.Entries.TryGetValue(tweakId, out var backup))
            {
                throw new InvalidOperationException("Backup inexistente.");
            }

            if (overwrite || !backup.Extras.ContainsKey(key))
            {
                backup.Extras[key] = value;
                Save();
            }
        }
    }

    /// <summary>Remove o backup (após reversão bem-sucedida).</summary>
    public void Remove(string tweakId)
    {
        lock (_gate)
        {
            if (_doc.Entries.Remove(tweakId))
            {
                Save();
            }
        }
    }

    /// <summary>Registra uma entrada no histórico.</summary>
    public void Journal(string action, string tweakId, string result, string? message = null)
    {
        lock (_gate)
        {
            _doc.Journal.Add(new JournalEntry
            {
                TimestampUtc = _clock.UtcNow,
                Action = action,
                TweakId = tweakId,
                Result = result,
                Message = message,
            });
            if (_doc.Journal.Count > MaxJournalEntries)
            {
                _doc.Journal.RemoveRange(0, _doc.Journal.Count - MaxJournalEntries);
            }

            Save();
        }
    }

    /// <summary>Grava a intenção de uma operação antes de agir. Retorna o ID da operação.</summary>
    public string BeginOperation(string tweakId, string action)
    {
        lock (_gate)
        {
            var op = new OperationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                TweakId = tweakId,
                Action = action,
                StartedUtc = _clock.UtcNow,
            };
            _doc.Operations.Add(op);
            Save();
            return op.Id;
        }
    }

    /// <summary>Marca a operação como concluída e descarta operações concluídas antigas.</summary>
    public void CompleteOperation(string operationId)
    {
        lock (_gate)
        {
            var op = _doc.Operations.FirstOrDefault(o => o.Id == operationId);
            if (op is null)
            {
                return;
            }

            op.CompletedUtc = _clock.UtcNow;
            _doc.Operations.RemoveAll(o => o.CompletedUtc is not null);
            Save();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_doc, JsonOptions);
        _fs.WriteAllTextAtomic(FilePath, json);
    }

    private static void Validate(BackupDocument doc)
    {
        if (doc.Schema != BackupDocument.CurrentSchema)
        {
            throw new InvalidDataException($"Schema de backup não suportado: {doc.Schema}.");
        }

        doc.Entries ??= new Dictionary<string, TweakBackup>(StringComparer.Ordinal);
        doc.Journal ??= [];
        doc.Operations ??= [];
        foreach (var (id, backup) in doc.Entries)
        {
            ValidateId(id);
            if (backup is null || !string.Equals(backup.TweakId, id, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Entrada de backup inconsistente.");
            }

            backup.Registry ??= [];
            backup.Extras ??= new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in backup.Registry)
            {
                if (string.IsNullOrWhiteSpace(r.KeyPath) || r.KeyPath.Contains("..", StringComparison.Ordinal) || r.Name is null)
                {
                    throw new InvalidDataException("Entrada de registro inválida.");
                }
            }
        }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 || !IdRegex().IsMatch(id))
        {
            throw new InvalidDataException($"ID de ajuste inválido: '{id}'.");
        }
    }

    private static TweakBackup Clone(TweakBackup b) =>
        JsonSerializer.Deserialize<TweakBackup>(JsonSerializer.Serialize(b, JsonOptions), JsonOptions)!;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(:[A-Za-z0-9_.-]+)?$")]
    private static partial Regex IdRegex();
}
