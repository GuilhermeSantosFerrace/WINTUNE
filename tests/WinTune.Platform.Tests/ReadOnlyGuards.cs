using WinTune.Platform.Abstractions;

namespace WinTune.Platform.Tests;

/// <summary>Envolve o registro real e lança em qualquer tentativa de escrita (prova de "somente leitura").</summary>
internal sealed class ReadOnlyRegistryGuard(IRegistry inner) : IRegistry
{
    public RegistryValue? GetValue(RegistryHive hive, string keyPath, string name) => inner.GetValue(hive, keyPath, name);

    public void SetValue(RegistryHive hive, string keyPath, string name, RegistryValue value) => throw Denied();

    public void DeleteValue(RegistryHive hive, string keyPath, string name) => throw Denied();

    public bool KeyExists(RegistryHive hive, string keyPath) => inner.KeyExists(hive, keyPath);

    public void CreateKey(RegistryHive hive, string keyPath) => throw Denied();

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string keyPath) => inner.GetValueNames(hive, keyPath);

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath) => inner.GetSubKeyNames(hive, keyPath);

    private static InvalidOperationException Denied() => new("Escrita no registro proibida neste teste.");
}

/// <summary>Executor que só permite comandos de leitura conhecidos.</summary>
internal sealed class ReadOnlyProcessGuard(IProcessRunner inner) : IProcessRunner
{
    private static readonly string[][] Allowed = [["powercfg.exe", "/list"], ["powercfg.exe", "/getactivescheme"]];

    public ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var ok = Allowed.Any(a => string.Equals(a[0], executable, StringComparison.OrdinalIgnoreCase) &&
                                  arguments.Count == 1 && string.Equals(a[1], arguments[0], StringComparison.OrdinalIgnoreCase));
        return ok ? inner.Run(executable, arguments, timeout, cancellationToken) : throw new InvalidOperationException($"Comando não permitido: {executable}");
    }
}
