using WinTune.Core.Backup;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Cleanup;

/// <summary>Categoria de limpeza com pastas raiz fixas (allowlist calculada a partir de pastas conhecidas, nunca de entrada do usuário).</summary>
public sealed record CleanupCategory
{
    /// <summary>ID.</summary>
    public required string Id { get; init; }

    /// <summary>Nome.</summary>
    public required string Name { get; init; }

    /// <summary>Descrição pt-BR.</summary>
    public required string Description { get; init; }

    /// <summary>Raízes permitidas.</summary>
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Padrão de arquivo.</summary>
    public string Pattern { get; init; } = "*";

    /// <summary>Recursivo.</summary>
    public bool Recursive { get; init; } = true;

    /// <summary>Idade mínima do arquivo (ex.: 24 h para temporários).</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    /// <summary>Marcada por padrão.</summary>
    public bool DefaultSelected { get; init; } = true;

    /// <summary>Aviso exibido na seleção.</summary>
    public string? Warning { get; init; }
}

/// <summary>Arquivo que será apagado.</summary>
/// <param name="CategoryId">Categoria.</param>
/// <param name="Path">Caminho real.</param>
/// <param name="Length">Tamanho.</param>
public sealed record CleanupCandidate(string CategoryId, string Path, long Length);

/// <summary>Arquivo ignorado e o motivo.</summary>
/// <param name="Path">Caminho.</param>
/// <param name="Reason">Motivo.</param>
public sealed record CleanupSkip(string Path, string Reason);

/// <summary>Prévia obrigatória. Só pode ser criada por <see cref="CleanupService.Preview"/>.</summary>
public sealed class CleanupPlan
{
    internal CleanupPlan(Guid owner, IReadOnlyList<CleanupCandidate> candidates, IReadOnlyList<CleanupSkip> skipped)
    {
        Owner = owner;
        Candidates = candidates;
        Skipped = skipped;
    }

    internal Guid Owner { get; }

    /// <summary>Arquivos a apagar.</summary>
    public IReadOnlyList<CleanupCandidate> Candidates { get; }

    /// <summary>Ignorados na prévia.</summary>
    public IReadOnlyList<CleanupSkip> Skipped { get; }

    /// <summary>Total previsto.</summary>
    public long TotalBytes => Candidates.Sum(c => c.Length);

    /// <summary>Total por categoria.</summary>
    public IReadOnlyDictionary<string, long> BytesByCategory =>
        Candidates.GroupBy(c => c.CategoryId).ToDictionary(g => g.Key, g => g.Sum(c => c.Length), StringComparer.Ordinal);
}

/// <summary>Relatório da limpeza.</summary>
/// <param name="FreedBytes">Bytes liberados.</param>
/// <param name="DeletedFiles">Arquivos apagados.</param>
/// <param name="Skipped">Ignorados e motivo.</param>
public sealed record CleanupReport(long FreedBytes, int DeletedFiles, IReadOnlyList<CleanupSkip> Skipped);

/// <summary>
/// Limpeza segura: prévia obrigatória, allowlist de raízes, proteção contra links/path traversal, arquivos em uso ignorados.
/// A limpeza NÃO é reversível (os arquivos são apagados; caches e temporários não vão para a Lixeira).
/// </summary>
public sealed class CleanupService
{
    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly BackupStore? _journal;
    private readonly Guid _id = Guid.NewGuid();

    /// <summary>Cria o serviço.</summary>
    public CleanupService(IFileSystem fs, IClock clock, IEnvironmentInfo env, BackupStore? journal = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _journal = journal;
        Categories = DefaultCategories(env ?? throw new ArgumentNullException(nameof(env)));
    }

    /// <summary>Categorias disponíveis.</summary>
    public IReadOnlyList<CleanupCategory> Categories { get; }

    /// <summary>Categorias padrão (seção Fase 9).</summary>
    public static IReadOnlyList<CleanupCategory> DefaultCategories(IEnvironmentInfo env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var local = env.GetFolder(KnownFolder.LocalAppData);
        var programData = env.GetFolder(KnownFolder.ProgramData);
        var windows = env.GetFolder(KnownFolder.Windows);
        return
        [
            new CleanupCategory
            {
                Id = "user-temp",
                Name = "Arquivos temporários do usuário",
                Description = "Arquivos em %TEMP% com mais de 24 horas. Arquivos em uso (ex.: instaladores abertos) são ignorados.",
                Roots = [env.GetFolder(KnownFolder.UserTemp)],
                MinAge = TimeSpan.FromHours(24),
            },
            new CleanupCategory
            {
                Id = "thumbnail-cache",
                Name = "Cache de miniaturas",
                Description = "Miniaturas do Explorador de Arquivos. O Windows recria quando necessário; arquivos em uso pelo Explorer são ignorados.",
                Roots = [Path.Combine(local, "Microsoft", "Windows", "Explorer")],
                Pattern = "thumbcache_*.db",
                Recursive = false,
            },
            new CleanupCategory
            {
                Id = "wer-reports",
                Name = "Relatórios de erro do Windows",
                Description = "Relatórios já gerados pelo Relatório de Erros do Windows (WER).",
                Roots =
                [
                    Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
                ],
            },
            new CleanupCategory
            {
                Id = "delivery-optimization",
                Name = "Cache do Otimização de Entrega",
                Description = "Arquivos de atualização compartilhados pelo Windows Update (se a pasta estiver acessível). Arquivos em uso são ignorados.",
                Roots = [Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")],
                DefaultSelected = false,
            },
            new CleanupCategory
            {
                Id = "shader-cache",
                Name = "Caches de shader (DirectX/NVIDIA)",
                Description = "Shaders compilados de jogos.",
                Roots =
                [
                    Path.Combine(local, "D3DSCache"),
                    Path.Combine(local, "NVIDIA", "DXCache"),
                    Path.Combine(local, "NVIDIA", "GLCache"),
                    Path.Combine(local + "Low", "NVIDIA", "PerDriverVersion", "DXCache"),
                ],
                DefaultSelected = false,
                Warning = "Depois de limpar, o primeiro carregamento de cada jogo fica mais lento e pode travar enquanto os shaders são recompilados. Só limpe se houver bug visual.",
            },
        ];
    }

    /// <summary>Gera a prévia (somente leitura).</summary>
    public CleanupPlan Preview(IEnumerable<string> categoryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categoryIds);
        var ids = categoryIds.ToHashSet(StringComparer.Ordinal);
        var candidates = new List<CleanupCandidate>();
        var skipped = new List<CleanupSkip>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = _clock.UtcNow.UtcDateTime;

        foreach (var category in Categories.Where(c => ids.Contains(c.Id)))
        {
            foreach (var root in category.Roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var realRoot = RealRoot(root);
                if (realRoot is null)
                {
                    continue;
                }

                foreach (var file in _fs.EnumerateFiles(root, category.Pattern, category.Recursive))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reason = Validate(file, realRoot, out var realPath);
                    if (reason is null && category.MinAge > TimeSpan.Zero && now - file.LastWriteTimeUtc < category.MinAge)
                    {
                        reason = "modificado nas últimas 24 h";
                    }

                    if (reason is not null)
                    {
                        skipped.Add(new CleanupSkip(file.FullPath, reason));
                    }
                    else if (seen.Add(realPath!))
                    {
                        candidates.Add(new CleanupCandidate(category.Id, realPath!, file.Length));
                    }
                }
            }
        }

        return new CleanupPlan(_id, candidates, skipped);
    }

    /// <summary>Executa uma prévia gerada por este serviço, revalidando cada arquivo. Irreversível.</summary>
    public CleanupReport Execute(CleanupPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Owner != _id)
        {
            throw new InvalidOperationException("A limpeza só aceita uma prévia gerada por este serviço.");
        }

        var roots = Categories.ToDictionary(c => c.Id, c => c.Roots.Select(RealRoot).OfType<string>().ToList(), StringComparer.Ordinal);
        long freed = 0;
        var deleted = 0;
        var skipped = new List<CleanupSkip>(plan.Skipped);
        foreach (var c in plan.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _fs.GetFile(c.Path);
            if (entry is null)
            {
                skipped.Add(new CleanupSkip(c.Path, "não existe mais"));
                continue;
            }

            string? reason = "categoria desconhecida";
            if (roots.TryGetValue(c.CategoryId, out var allowed))
            {
                reason = "fora das pastas permitidas";
                foreach (var root in allowed)
                {
                    var r = Validate(entry, root, out _);
                    if (r is null || r == "em uso")
                    {
                        reason = r;
                        break;
                    }
                }
            }

            if (reason is not null)
            {
                skipped.Add(new CleanupSkip(c.Path, reason));
                continue;
            }

            try
            {
                _fs.DeleteFile(c.Path);
                freed += entry.Length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new CleanupSkip(c.Path, "não pôde ser apagado: " + ex.Message));
            }
        }

        _journal?.Journal("cleanup", "-", "success", $"{deleted} arquivo(s), {freed} bytes liberados, {skipped.Count} ignorado(s)");
        return new CleanupReport(freed, deleted, skipped);
    }

    private string? RealRoot(string root)
    {
        if (!_fs.DirectoryExists(root))
        {
            return null;
        }

        var real = _fs.ResolveFinalPath(root);
        return real is null ? null : Path.TrimEndingDirectorySeparator(real);
    }

    private string? Validate(FileEntry file, string realRoot, out string? realPath)
    {
        realPath = null;
        if (file.IsReparsePoint)
        {
            return "é um link simbólico/junção";
        }

        var full = Path.GetFullPath(file.FullPath);
        realPath = _fs.ResolveFinalPath(full);
        if (realPath is null)
        {
            return "caminho real não pôde ser resolvido";
        }

        if (!realPath.StartsWith(realRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "fora das pastas permitidas (link ou caminho suspeito)";
        }

        return _fs.IsFileLocked(realPath) ? "em uso" : null;
    }
}
