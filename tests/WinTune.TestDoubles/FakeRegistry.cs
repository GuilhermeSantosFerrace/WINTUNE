using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>Registro em memória com injeção de falhas.</summary>
public sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<(RegistryHive, string), Dictionary<string, RegistryValue>> _keys = new();

    /// <summary>Quando retorna verdadeiro, <see cref="SetValue"/> lança <see cref="UnauthorizedAccessException"/>.</summary>
    public Func<RegistryHive, string, string, bool>? FailOnSet { get; set; }

    /// <summary>Quantidade de gravações bem-sucedidas.</summary>
    public int WriteCount { get; private set; }

    public RegistryValue? GetValue(RegistryHive hive, string keyPath, string name) =>
        _keys.TryGetValue(Key(hive, keyPath), out var values) && values.TryGetValue(name, out var v) ? v : null;

    public void SetValue(RegistryHive hive, string keyPath, string name, RegistryValue value)
    {
        if (FailOnSet?.Invoke(hive, keyPath, name) == true)
        {
            throw new UnauthorizedAccessException($"Falha simulada em {hive}\\{keyPath}\\{name}");
        }

        CreateKey(hive, keyPath);
        _keys[Key(hive, keyPath)][name] = value;
        WriteCount++;
    }

    public void DeleteValue(RegistryHive hive, string keyPath, string name)
    {
        if (_keys.TryGetValue(Key(hive, keyPath), out var values))
        {
            values.Remove(name);
        }
    }

    public bool KeyExists(RegistryHive hive, string keyPath) => _keys.ContainsKey(Key(hive, keyPath));

    public void CreateKey(RegistryHive hive, string keyPath)
    {
        var parts = Normalize(keyPath).Split('\\');
        for (var i = 1; i <= parts.Length; i++)
        {
            var k = (hive, string.Join('\\', parts.Take(i)));
            if (!_keys.ContainsKey(k))
            {
                _keys[k] = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath) =>
        _keys.TryGetValue(Key(hive, keyPath), out var values) ? values.Keys.ToList() : [];

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        var prefix = Normalize(keyPath) + "\\";
        return _keys.Keys
            .Where(k => k.Item1 == hive && k.Item2.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k.Item2[prefix.Length..])
            .Where(rest => !rest.Contains('\\', StringComparison.Ordinal))
            .Select(rest => rest)
            .ToList();
    }

    /// <summary>Atalho: grava um DWORD.</summary>
    public FakeRegistry With(RegistryHive hive, string keyPath, string name, int value)
    {
        SetValue(hive, keyPath, name, RegistryValue.FromDWord(value));
        return this;
    }

    /// <summary>Atalho: grava um texto.</summary>
    public FakeRegistry With(RegistryHive hive, string keyPath, string name, string value)
    {
        SetValue(hive, keyPath, name, RegistryValue.FromString(value));
        return this;
    }

    private static (RegistryHive, string) Key(RegistryHive hive, string keyPath) => (hive, Normalize(keyPath));

    private static string Normalize(string keyPath) => keyPath.Trim('\\').ToLowerInvariant();
}
