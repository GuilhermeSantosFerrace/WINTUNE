using Microsoft.Win32;
using WinTune.Platform.Abstractions;
using AbsHive = WinTune.Platform.Abstractions.RegistryHive;
using AbsKind = WinTune.Platform.Abstractions.RegistryValueKind;
using Win32Kind = Microsoft.Win32.RegistryValueKind;

namespace WinTune.Platform;

/// <summary>Implementação de <see cref="IRegistry"/> sobre o registro real (visão de 64 bits).</summary>
public sealed class WindowsRegistry : IRegistry
{
    /// <inheritdoc />
    public RegistryValue? GetValue(AbsHive hive, string keyPath, string name)
    {
        using var root = OpenBase(hive);
        using var key = root.OpenSubKey(Normalize(keyPath), writable: false);
        if (key is null)
        {
            return null;
        }

        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return null;
        }

        return key.GetValueKind(name) switch
        {
            Win32Kind.DWord => RegistryValue.FromDWord(Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture)),
            Win32Kind.String => RegistryValue.FromString((string)raw),
            Win32Kind.ExpandString => RegistryValue.FromExpandString((string)raw),
            Win32Kind.Binary => RegistryValue.FromBinary((byte[])raw),
            _ => RegistryValue.Unsupported,
        };
    }

    /// <inheritdoc />
    public void SetValue(AbsHive hive, string keyPath, string name, RegistryValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var root = OpenBase(hive);
        using var key = root.CreateSubKey(Normalize(keyPath), writable: true)
            ?? throw new InvalidOperationException($"Não foi possível abrir {hive}\\{keyPath} para escrita.");

        switch (value.Kind)
        {
            case AbsKind.DWord:
                key.SetValue(name, value.DWord!.Value, Win32Kind.DWord);
                break;
            case AbsKind.String:
                key.SetValue(name, value.Text!, Win32Kind.String);
                break;
            case AbsKind.ExpandString:
                key.SetValue(name, value.Text!, Win32Kind.ExpandString);
                break;
            case AbsKind.Binary:
                key.SetValue(name, value.Binary!, Win32Kind.Binary);
                break;
            default:
                throw new NotSupportedException("Tipo de valor de registro não suportado para escrita.");
        }
    }

    /// <inheritdoc />
    public void DeleteValue(AbsHive hive, string keyPath, string name)
    {
        using var root = OpenBase(hive);
        using var key = root.OpenSubKey(Normalize(keyPath), writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public bool KeyExists(AbsHive hive, string keyPath)
    {
        using var root = OpenBase(hive);
        using var key = root.OpenSubKey(Normalize(keyPath), writable: false);
        return key is not null;
    }

    /// <inheritdoc />
    public void CreateKey(AbsHive hive, string keyPath)
    {
        using var root = OpenBase(hive);
        using var key = root.CreateSubKey(Normalize(keyPath), writable: true);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetValueNames(AbsHive hive, string keyPath)
    {
        using var root = OpenBase(hive);
        using var key = root.OpenSubKey(Normalize(keyPath), writable: false);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSubKeyNames(AbsHive hive, string keyPath)
    {
        using var root = OpenBase(hive);
        using var key = root.OpenSubKey(Normalize(keyPath), writable: false);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    private static RegistryKey OpenBase(AbsHive hive) => hive switch
    {
        AbsHive.CurrentUser => RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, RegistryView.Registry64),
        AbsHive.LocalMachine => RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry64),
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };

    private static string Normalize(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        if (keyPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Caminho de chave inválido.", nameof(keyPath));
        }

        return keyPath.Trim('\\');
    }
}
