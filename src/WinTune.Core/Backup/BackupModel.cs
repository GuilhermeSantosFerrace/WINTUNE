using WinTune.Platform.Abstractions;

namespace WinTune.Core.Backup;

/// <summary>Valor de registro serializável.</summary>
public sealed class StoredRegistryValue
{
    /// <summary>Tipo.</summary>
    public RegistryValueKind Kind { get; set; }

    /// <summary>Dado DWORD.</summary>
    public int? DWord { get; set; }

    /// <summary>Dado texto.</summary>
    public string? Text { get; set; }

    /// <summary>Dado binário em hexadecimal.</summary>
    public string? BinaryHex { get; set; }

    /// <summary>Converte de <see cref="RegistryValue"/> (null = valor inexistente).</summary>
    public static StoredRegistryValue? From(RegistryValue? value) => value is null
        ? null
        : new StoredRegistryValue
        {
            Kind = value.Kind,
            DWord = value.DWord,
            Text = value.Text,
            BinaryHex = value.Binary is null ? null : Convert.ToHexString(value.Binary),
        };

    /// <summary>Converte para <see cref="RegistryValue"/>; lança se o conteúdo for inconsistente.</summary>
    public RegistryValue ToValue() => Kind switch
    {
        RegistryValueKind.DWord => RegistryValue.FromDWord(DWord ?? throw new InvalidDataException("DWORD sem dado.")),
        RegistryValueKind.String => RegistryValue.FromString(Text ?? throw new InvalidDataException("Texto sem dado.")),
        RegistryValueKind.ExpandString => RegistryValue.FromExpandString(Text ?? throw new InvalidDataException("Texto sem dado.")),
        RegistryValueKind.Binary => RegistryValue.FromBinary(Convert.FromHexString(BinaryHex ?? throw new InvalidDataException("Binário sem dado."))),
        _ => throw new InvalidDataException("Tipo de valor não restaurável."),
    };
}

/// <summary>Backup de um valor de registro: original e valor aplicado pelo WinTune.</summary>
public sealed class RegistryBackupEntry
{
    /// <summary>Hive.</summary>
    public RegistryHive Hive { get; set; }

    /// <summary>Caminho da chave.</summary>
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>Nome do valor.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Valor original (null = não existia).</summary>
    public StoredRegistryValue? Original { get; set; }

    /// <summary>Valor gravado pelo WinTune (para detectar drift).</summary>
    public StoredRegistryValue? Applied { get; set; }

    /// <summary>Indica se a entrada corresponde ao mesmo (hive, chave, nome), sem diferenciar maiúsculas.</summary>
    public bool Matches(RegistryHive hive, string keyPath, string name) =>
        Hive == hive &&
        string.Equals(KeyPath.Trim('\\'), keyPath.Trim('\\'), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Backup completo de um ajuste.</summary>
public sealed class TweakBackup
{
    /// <summary>ID do ajuste.</summary>
    public string TweakId { get; set; } = string.Empty;

    /// <summary>Quando o original foi gravado.</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Valores de registro originais.</summary>
    public List<RegistryBackupEntry> Registry { get; set; } = [];

    /// <summary>Extras (ex.: GUID do plano de energia anterior, estado anterior de serviço).</summary>
    public Dictionary<string, string> Extras { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Entrada do histórico de operações.</summary>
public sealed class JournalEntry
{
    /// <summary>Momento (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Ação (apply, revert, restore-point, ...).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>ID do ajuste (ou "-" para operações gerais).</summary>
    public string TweakId { get; set; } = string.Empty;

    /// <summary>Resultado.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Mensagem.</summary>
    public string? Message { get; set; }
}

/// <summary>Intenção de operação gravada antes de agir (para reconciliação após queda, R13).</summary>
public sealed class OperationRecord
{
    /// <summary>ID da operação.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>ID do ajuste.</summary>
    public string TweakId { get; set; } = string.Empty;

    /// <summary>Ação.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Início (UTC).</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>Término (null = não concluída).</summary>
    public DateTimeOffset? CompletedUtc { get; set; }
}

/// <summary>Documento JSON do backup.</summary>
public sealed class BackupDocument
{
    /// <summary>Versão do schema atual.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Versão do schema.</summary>
    public int Schema { get; set; } = CurrentSchema;

    /// <summary>Backups por ID de ajuste.</summary>
    public Dictionary<string, TweakBackup> Entries { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Histórico.</summary>
    public List<JournalEntry> Journal { get; set; } = [];

    /// <summary>Operações (intenções).</summary>
    public List<OperationRecord> Operations { get; set; } = [];
}
