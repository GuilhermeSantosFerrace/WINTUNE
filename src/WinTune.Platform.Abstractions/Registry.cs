namespace WinTune.Platform.Abstractions;

/// <summary>Hives de registro que o WinTune pode ler ou alterar.</summary>
public enum RegistryHive
{
    /// <summary>HKEY_CURRENT_USER.</summary>
    CurrentUser,

    /// <summary>HKEY_LOCAL_MACHINE (visão de 64 bits).</summary>
    LocalMachine,
}

/// <summary>Tipos de valor de registro suportados.</summary>
public enum RegistryValueKind
{
    /// <summary>REG_DWORD (32 bits).</summary>
    DWord,

    /// <summary>REG_SZ.</summary>
    String,

    /// <summary>REG_EXPAND_SZ (lido sem expandir variáveis).</summary>
    ExpandString,

    /// <summary>REG_BINARY.</summary>
    Binary,

    /// <summary>Qualquer outro tipo (somente leitura, nunca gravado pelo WinTune).</summary>
    Unsupported,
}

/// <summary>
/// Valor imutável de registro com igualdade por conteúdo (inclusive para binários).
/// </summary>
public sealed class RegistryValue : IEquatable<RegistryValue>
{
    private readonly byte[]? _binary;

    private RegistryValue(RegistryValueKind kind, int? dword, string? text, byte[]? binary)
    {
        Kind = kind;
        DWord = dword;
        Text = text;
        _binary = binary;
    }

    /// <summary>Tipo do valor.</summary>
    public RegistryValueKind Kind { get; }

    /// <summary>Dado DWORD, quando <see cref="Kind"/> é <see cref="RegistryValueKind.DWord"/>.</summary>
    public int? DWord { get; }

    /// <summary>Dado texto, quando o tipo é String ou ExpandString.</summary>
    public string? Text { get; }

    /// <summary>Cópia do dado binário, quando o tipo é Binary.</summary>
    public byte[]? Binary => _binary is null ? null : (byte[])_binary.Clone();

    /// <summary>Cria um valor REG_DWORD.</summary>
    public static RegistryValue FromDWord(int value) => new(RegistryValueKind.DWord, value, null, null);

    /// <summary>Cria um valor REG_SZ.</summary>
    public static RegistryValue FromString(string value) =>
        new(RegistryValueKind.String, null, value ?? throw new ArgumentNullException(nameof(value)), null);

    /// <summary>Cria um valor REG_EXPAND_SZ.</summary>
    public static RegistryValue FromExpandString(string value) =>
        new(RegistryValueKind.ExpandString, null, value ?? throw new ArgumentNullException(nameof(value)), null);

    /// <summary>Cria um valor REG_BINARY (o array é copiado).</summary>
    public static RegistryValue FromBinary(byte[] value) =>
        new(RegistryValueKind.Binary, null, null, (byte[])(value ?? throw new ArgumentNullException(nameof(value))).Clone());

    /// <summary>Representa um tipo não suportado (somente leitura).</summary>
    public static RegistryValue Unsupported { get; } = new(RegistryValueKind.Unsupported, null, null, null);

    /// <inheritdoc />
    public bool Equals(RegistryValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Kind != other.Kind || DWord != other.DWord || !string.Equals(Text, other.Text, StringComparison.Ordinal))
        {
            return false;
        }

        if (_binary is null || other._binary is null)
        {
            return _binary is null && other._binary is null;
        }

        return _binary.AsSpan().SequenceEqual(other._binary);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RegistryValue);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(DWord);
        hash.Add(Text, StringComparer.Ordinal);
        if (_binary is not null)
        {
            hash.AddBytes(_binary);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        RegistryValueKind.DWord => $"DWORD {DWord}",
        RegistryValueKind.String => $"SZ \"{Text}\"",
        RegistryValueKind.ExpandString => $"EXPAND_SZ \"{Text}\"",
        RegistryValueKind.Binary => $"BINARY {Convert.ToHexString(_binary!)}",
        _ => "UNSUPPORTED",
    };
}

/// <summary>Acesso ao registro do Windows. Caminhos de chave são relativos ao hive, sem barra inicial.</summary>
public interface IRegistry
{
    /// <summary>Lê um valor; retorna <c>null</c> se a chave ou o valor não existir.</summary>
    RegistryValue? GetValue(RegistryHive hive, string keyPath, string name);

    /// <summary>Grava um valor, criando a chave se necessário.</summary>
    void SetValue(RegistryHive hive, string keyPath, string name, RegistryValue value);

    /// <summary>Apaga um valor; não faz nada se ele não existir.</summary>
    void DeleteValue(RegistryHive hive, string keyPath, string name);

    /// <summary>Indica se a chave existe.</summary>
    bool KeyExists(RegistryHive hive, string keyPath);

    /// <summary>Cria a chave (e as intermediárias) se não existir.</summary>
    void CreateKey(RegistryHive hive, string keyPath);

    /// <summary>Lista os nomes de valores da chave (vazio se a chave não existir).</summary>
    IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath);

    /// <summary>Lista as subchaves (vazio se a chave não existir).</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath);
}

/// <summary>Atalhos tipados sobre <see cref="IRegistry"/>.</summary>
public static class RegistryExtensions
{
    /// <summary>Lê um DWORD; <c>null</c> se ausente ou de outro tipo.</summary>
    public static int? GetDWord(this IRegistry registry, RegistryHive hive, string keyPath, string name)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var value = registry.GetValue(hive, keyPath, name);
        return value?.Kind == RegistryValueKind.DWord ? value.DWord : null;
    }

    /// <summary>Lê um texto (REG_SZ ou REG_EXPAND_SZ); <c>null</c> se ausente ou de outro tipo.</summary>
    public static string? GetString(this IRegistry registry, RegistryHive hive, string keyPath, string name)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var value = registry.GetValue(hive, keyPath, name);
        return value?.Kind is RegistryValueKind.String or RegistryValueKind.ExpandString ? value.Text : null;
    }

    /// <summary>Grava um DWORD.</summary>
    public static void SetDWord(this IRegistry registry, RegistryHive hive, string keyPath, string name, int value)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.SetValue(hive, keyPath, name, RegistryValue.FromDWord(value));
    }

    /// <summary>Grava um REG_SZ.</summary>
    public static void SetString(this IRegistry registry, RegistryHive hive, string keyPath, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.SetValue(hive, keyPath, name, RegistryValue.FromString(value));
    }
}
