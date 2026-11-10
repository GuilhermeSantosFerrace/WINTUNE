using System.Text.RegularExpressions;
using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>WMI simulado: linhas registradas por classe (cláusula FROM).</summary>
public sealed partial class FakeWmiReader : IWmiReader
{
    private readonly Dictionary<string, List<WmiRow>> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Consultas recebidas.</summary>
    public List<string> Queries { get; } = [];

    /// <summary>Adiciona uma linha para a classe WMI.</summary>
    public FakeWmiReader Add(string wmiClass, params (string Name, object? Value)[] properties)
    {
        if (!_rows.TryGetValue(wmiClass, out var list))
        {
            _rows[wmiClass] = list = [];
        }

        list.Add(new WmiRow(properties.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase)));
        return this;
    }

    /// <summary>Faz a consulta à classe lançar exceção.</summary>
    public FakeWmiReader Fail(string wmiClass)
    {
        _failing.Add(wmiClass);
        return this;
    }

    public IReadOnlyList<WmiRow> Query(string scope, string wql)
    {
        Queries.Add(wql);
        var cls = FromRegex().Match(wql).Groups[1].Value;
        if (_failing.Contains(cls))
        {
            throw new InvalidOperationException($"Falha WMI simulada em {cls}");
        }

        return _rows.TryGetValue(cls, out var list) ? list.ToList() : [];
    }

    [GeneratedRegex(@"\bFROM\s+(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromRegex();
}

/// <summary>Controle de serviços simulado.</summary>
public sealed class FakeServiceControl : IServiceControl
{
    /// <summary>Serviços por nome.</summary>
    public Dictionary<string, ServiceInfo> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Quantidade de alterações.</summary>
    public int SetCount { get; private set; }

    public FakeServiceControl Add(string name, ServiceStartMode mode, string? imagePath = null, bool delayed = false, string state = "Running", int pid = 0)
    {
        Services[name] = new ServiceInfo(name, name, state, new ServiceConfig(mode, delayed), imagePath, pid);
        return this;
    }

    public IReadOnlyList<ServiceInfo> GetServices() => Services.Values.ToList();

    public ServiceConfig? GetConfig(string serviceName) => Services.TryGetValue(serviceName, out var s) ? s.Config : null;

    public void SetConfig(string serviceName, ServiceConfig config)
    {
        if (!Services.TryGetValue(serviceName, out var s))
        {
            throw new InvalidOperationException("Serviço inexistente.");
        }

        Services[serviceName] = s with { Config = config };
        SetCount++;
    }
}
