namespace WinTune.Platform.Abstractions;

/// <summary>Metadados de um arquivo.</summary>
/// <param name="FullPath">Caminho completo.</param>
/// <param name="Length">Tamanho em bytes.</param>
/// <param name="LastWriteTimeUtc">Última modificação (UTC).</param>
/// <param name="IsReparsePoint">Indica link simbólico/junção.</param>
public sealed record FileEntry(string FullPath, long Length, DateTime LastWriteTimeUtc, bool IsReparsePoint);

/// <summary>Espaço de um volume.</summary>
/// <param name="TotalBytes">Capacidade total.</param>
/// <param name="FreeBytes">Espaço livre disponível.</param>
public sealed record DriveSpace(long TotalBytes, long FreeBytes)
{
    /// <summary>Fração livre (0..1).</summary>
    public double FreeFraction => TotalBytes <= 0 ? 0 : (double)FreeBytes / TotalBytes;
}

/// <summary>Sistema de arquivos. Implementações nunca seguem links simbólicos ao enumerar.</summary>
public interface IFileSystem
{
    /// <summary>Indica se o arquivo existe.</summary>
    bool FileExists(string path);

    /// <summary>Indica se o diretório existe.</summary>
    bool DirectoryExists(string path);

    /// <summary>Metadados do arquivo ou <c>null</c>.</summary>
    FileEntry? GetFile(string path);

    /// <summary>
    /// Enumera arquivos. Em modo recursivo, diretórios que são links/junções não são percorridos;
    /// arquivos que são links aparecem com <see cref="FileEntry.IsReparsePoint"/> verdadeiro.
    /// Diretórios inacessíveis são ignorados.
    /// </summary>
    IEnumerable<FileEntry> EnumerateFiles(string directory, string searchPattern, bool recursive);

    /// <summary>Subdiretórios imediatos (vazio se não existir).</summary>
    IEnumerable<string> EnumerateDirectories(string directory);

    /// <summary>Lê todo o texto (UTF-8).</summary>
    string ReadAllText(string path);

    /// <summary>Grava texto de forma atômica (arquivo temporário + substituição).</summary>
    void WriteAllTextAtomic(string path, string content);

    /// <summary>Move/renomeia um arquivo.</summary>
    void MoveFile(string source, string destination, bool overwrite);

    /// <summary>Apaga um arquivo.</summary>
    void DeleteFile(string path);

    /// <summary>Cria o diretório (e intermediários).</summary>
    void CreateDirectory(string path);

    /// <summary>Caminho real após resolver links/junções; <c>null</c> se não for possível.</summary>
    string? ResolveFinalPath(string path);

    /// <summary>Indica se o arquivo está em uso (não pode ser aberto com acesso exclusivo).</summary>
    bool IsFileLocked(string path);

    /// <summary>Espaço do volume que contém <paramref name="path"/>; <c>null</c> se indisponível.</summary>
    DriveSpace? GetDriveSpace(string path);

    /// <summary>Campo "CompanyName" das informações de versão do executável.</summary>
    string? GetFileCompany(string path);

    /// <summary>Assunto (subject) do certificado Authenticode embutido, sem validar a cadeia.</summary>
    string? GetSignerSubject(string path);

    /// <summary>
    /// Cria o diretório e restringe a escrita a Administradores e SYSTEM (usuários: somente leitura).
    /// Usado para guardar o backup (anti-adulteração).
    /// </summary>
    void EnsureAdminOnlyDirectory(string path);
}
