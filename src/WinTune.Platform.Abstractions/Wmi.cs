using System.Globalization;

namespace WinTune.Platform.Abstractions;

/// <summary>Uma linha de resultado WMI com leitores tipados.</summary>
public sealed class WmiRow
{
    private readonly IReadOnlyDictionary<string, object?> _values;

    /// <summary>Cria a linha a partir de propriedades (nomes sem diferenciar maiúsculas).</summary>
    public WmiRow(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nomes das propriedades presentes.</summary>
    public IEnumerable<string> PropertyNames => _values.Keys;

    /// <summary>Valor bruto de uma propriedade (ou <c>null</c>).</summary>
    public object? this[string name] => _values.TryGetValue(name, out var v) ? v : null;

    /// <summary>Lê como texto.</summary>
    public string? GetString(string name) => this[name] switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var o => o.ToString(),
    };

    /// <summary>Lê como inteiro sem sinal de 64 bits.</summary>
    public ulong? GetUInt64(string name) => this[name] switch
    {
        null => null,
        string s => ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null,
        IConvertible c => TryConvert(c),
        _ => null,
    };

    /// <summary>Lê como inteiro de 64 bits.</summary>
    public long? GetInt64(string name)
    {
        var v = GetUInt64(name);
        return v is null || v > long.MaxValue ? null : (long)v.Value;
    }

    /// <summary>Lê como booleano.</summary>
    public bool? GetBoolean(string name) => this[name] switch
    {
        null => null,
        bool b => b,
        string s => bool.TryParse(s, out var r) ? r : null,
        _ => null,
    };

    /// <summary>Lê um array numérico (ex.: <c>SecurityServicesRunning</c>).</summary>
    public IReadOnlyList<long> GetInt64Array(string name)
    {
        if (this[name] is not Array arr)
        {
            return Array.Empty<long>();
        }

        var list = new List<long>(arr.Length);
        foreach (var item in arr)
        {
            if (item is IConvertible c)
            {
                list.Add(c.ToInt64(CultureInfo.InvariantCulture));
            }
        }

        return list;
    }

    private static ulong? TryConvert(IConvertible c)
    {
        try
        {
            return c.ToUInt64(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}

/// <summary>Consultas WMI somente leitura.</summary>
public interface IWmiReader
{
    /// <summary>
    /// Executa uma consulta WQL no namespace <paramref name="scope"/> (ex.: <c>root\cimv2</c>).
    /// Lança exceção se o namespace/classe não existir ou houver erro de acesso.
    /// </summary>
    IReadOnlyList<WmiRow> Query(string scope, string wql);
}

/// <summary>Namespaces WMI usados pelo WinTune.</summary>
public static class WmiScopes
{
    /// <summary><c>root\cimv2</c>.</summary>
    public const string Cimv2 = @"root\cimv2";

    /// <summary><c>root\Microsoft\Windows\DeviceGuard</c>.</summary>
    public const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";

    /// <summary><c>root\Microsoft\Windows\Storage</c>.</summary>
    public const string Storage = @"root\Microsoft\Windows\Storage";

    /// <summary><c>root\wmi</c>.</summary>
    public const string Wmi = @"root\wmi";
}
