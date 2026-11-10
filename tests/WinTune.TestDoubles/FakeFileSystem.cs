using System.Text.RegularExpressions;
using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>Sistema de arquivos em memória com suporte a links, arquivos bloqueados e falhas.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, FakeFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reparseDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _finalPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivos apagados.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>Diretórios criados com ACL restrita.</summary>
    public List<string> SecuredDirectories { get; } = [];

    /// <summary>Se verdadeiro, <see cref="EnsureAdminOnlyDirectory"/> lança (simula falta de elevação).</summary>
    public bool FailSecureDirectory { get; set; }

    /// <summary>Volumes por raiz (ex.: <c>C:\</c>).</summary>
    public Dictionary<string, DriveSpace> Drives { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adiciona um arquivo.</summary>
    public FakeFileSystem AddFile(string path, string content = "", long? length = null, DateTime? lastWriteUtc = null,
        bool locked = false, bool reparse = false, string? company = null, string? signer = null)
    {
        _files[Norm(path)] = new FakeFile(content, length ?? content.Length, lastWriteUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            locked, reparse, company, signer);
        AddDirectory(Path.GetDirectoryName(Norm(path))!);
        return this;
    }

    /// <summary>Adiciona um diretório (e ancestrais).</summary>
    public FakeFileSystem AddDirectory(string path, bool reparse = false)
    {
        var p = Norm(path);
        while (!string.IsNullOrEmpty(p))
        {
            _dirs.Add(p);
            p = Path.GetDirectoryName(p) ?? string.Empty;
        }

        if (reparse)
        {
            _reparseDirs.Add(Norm(path));
        }

        return this;
    }

    /// <summary>Define o caminho real de <paramref name="path"/> (simula link).</summary>
    public FakeFileSystem MapFinalPath(string path, string finalPath)
    {
        _finalPaths[Norm(path)] = Norm(finalPath);
        return this;
    }

    /// <summary>Conteúdo atual de um arquivo.</summary>
    public string? Content(string path) => _files.TryGetValue(Norm(path), out var f) ? f.Content : null;

    public bool FileExists(string path) => _files.ContainsKey(Norm(path));

    public bool DirectoryExists(string path) => _dirs.Contains(Norm(path));

    public FileEntry? GetFile(string path) => _files.TryGetValue(Norm(path), out var f) ? ToEntry(Norm(path), f) : null;

    public IEnumerable<FileEntry> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        var dir = Norm(directory);
        var regex = new Regex("^" + Regex.Escape(searchPattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase);
        foreach (var (path, file) in _files.ToList())
        {
            var parent = Path.GetDirectoryName(path)!;
            var inScope = recursive
                ? parent.Equals(dir, StringComparison.OrdinalIgnoreCase) || parent.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)
                : parent.Equals(dir, StringComparison.OrdinalIgnoreCase);
            if (!inScope || !regex.IsMatch(Path.GetFileName(path)))
            {
                continue;
            }

            if (recursive && CrossesReparseDir(dir, parent))
            {
                continue;
            }

            yield return ToEntry(path, file);
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var dir = Norm(directory);
        return _dirs.Where(d => string.Equals(Path.GetDirectoryName(d), dir, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public string ReadAllText(string path) =>
        _files.TryGetValue(Norm(path), out var f) ? f.Content : throw new FileNotFoundException(path);

    public void WriteAllTextAtomic(string path, string content) => AddFile(path, content);

    public void MoveFile(string source, string destination, bool overwrite)
    {
        var s = Norm(source);
        if (!_files.Remove(s, out var f))
        {
            throw new FileNotFoundException(source);
        }

        if (!overwrite && _files.ContainsKey(Norm(destination)))
        {
            throw new IOException("destino existe");
        }

        _files[Norm(destination)] = f;
    }

    public void DeleteFile(string path)
    {
        var p = Norm(path);
        if (_files.TryGetValue(p, out var f) && f.Locked)
        {
            throw new IOException("arquivo em uso");
        }

        if (_files.Remove(p))
        {
            Deleted.Add(p);
        }
    }

    public void CreateDirectory(string path) => AddDirectory(path);

    public string? ResolveFinalPath(string path)
    {
        var p = Norm(path);
        if (_finalPaths.TryGetValue(p, out var mapped))
        {
            return mapped;
        }

        // Resolve o diretório ancestral mapeado, se houver.
        foreach (var (from, to) in _finalPaths)
        {
            if (p.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return to + p[from.Length..];
            }
        }

        return _files.ContainsKey(p) || _dirs.Contains(p) ? p : null;
    }

    public bool IsFileLocked(string path) => _files.TryGetValue(Norm(path), out var f) && f.Locked;

    public DriveSpace? GetDriveSpace(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return Drives.TryGetValue(root, out var d) ? d : null;
    }

    public string? GetFileCompany(string path) => _files.TryGetValue(Norm(path), out var f) ? f.Company : null;

    public string? GetSignerSubject(string path) => _files.TryGetValue(Norm(path), out var f) ? f.Signer : null;

    public void EnsureAdminOnlyDirectory(string path)
    {
        if (FailSecureDirectory)
        {
            throw new UnauthorizedAccessException("sem elevação");
        }

        AddDirectory(path);
        SecuredDirectories.Add(Norm(path));
    }

    private bool CrossesReparseDir(string root, string parent)
    {
        var p = parent;
        while (p.Length > root.Length)
        {
            if (_reparseDirs.Contains(p))
            {
                return true;
            }

            p = Path.GetDirectoryName(p)!;
        }

        return false;
    }

    private static FileEntry ToEntry(string path, FakeFile f) => new(path, f.Length, f.LastWriteUtc, f.Reparse);

    private static string Norm(string path) => Path.TrimEndingDirectorySeparator(path.Replace('/', '\\'));

    private sealed record FakeFile(string Content, long Length, DateTime LastWriteUtc, bool Locked, bool Reparse, string? Company, string? Signer);
}
