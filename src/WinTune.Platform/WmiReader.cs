using System.Management;
using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>Leitor WMI somente leitura baseado em <c>System.Management</c>.</summary>
public sealed class WmiReader : IWmiReader
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public IReadOnlyList<WmiRow> Query(string scope, string wql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(wql);
        if (!wql.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Somente consultas SELECT são permitidas.", nameof(wql));
        }

        var options = new System.Management.EnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = true, Rewindable = false };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        var rows = new List<WmiRow>();
        foreach (var obj in results)
        {
            using (obj)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in obj.Properties)
                {
                    dict[prop.Name] = prop.Value;
                }

                rows.Add(new WmiRow(dict));
            }
        }

        return rows;
    }
}
