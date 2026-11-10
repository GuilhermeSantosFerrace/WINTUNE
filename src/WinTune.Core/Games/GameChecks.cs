using System.Globalization;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Games;

/// <summary>Verificações (Check) e guias (Guia) da aba Jogos (seção 4.4). Nunca alteram o sistema nem tocam no jogo.</summary>
public static class GameChecks
{
    /// <summary>Monta as regras para os jogos selecionados.</summary>
    public static IReadOnlyList<IDiagnosticRule> Rules(IReadOnlyList<GameDefinition> definitions, GameTarget selected)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var games = definitions.Where(d => selected == GameTarget.None || (selected & d.Target) != 0).ToList();
        var all = games.Aggregate(GameTarget.None, (acc, g) => acc | g.Target);
        if (games.Count == 0)
        {
            return [];
        }

        var rules = new List<IDiagnosticRule>
        {
            new GameInstallDriveRule(games),
            new GameRefreshRateRule(all),
            new GameRamHeadroomRule(all),
            new GameShaderCacheStateRule(all),
            new GpuDriverOldRule("game-driver-age", all),
            new GameGpuPreferenceGuide(all),
            new GameFsoCompatGuide(all),
            new GameNvcpGuide(all),
        };
        if ((all & GameTarget.RainbowSixSiege) != 0)
        {
            rules.Add(new GameGuideSiege());
        }

        if ((all & GameTarget.Cs2) != 0)
        {
            rules.Add(new GameGuideCs2());
        }

        return rules;
    }

    internal static Finding Game(string id, string title, string message, Severity severity, GameTarget games, string? evidence = null) =>
        new(id, title, message, severity, evidence, games, FindingCategory.Game);

    internal static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " MB",
        _ => (bytes / 1024.0).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " KB",
    };
}

/// <summary><c>game-install-drive</c>: avisa se o jogo está em HDD, USB, SATA ou disco quase cheio.</summary>
public sealed class GameInstallDriveRule(IReadOnlyList<GameDefinition> games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-install-drive";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var locator = new GameInstallLocator(context.Registry, context.FileSystem);
        foreach (var game in games)
        {
            var installs = locator.Locate(game);
            if (installs.Count == 0)
            {
                yield return GameChecks.Game(Id, $"{game.DisplayName}: instalação não encontrada",
                    "Não encontrei a instalação pela biblioteca da Steam nem pelo Ubisoft Connect. As demais verificações continuam valendo.",
                    Severity.Info, game.Target);
                continue;
            }

            foreach (var dir in installs)
            {
                var root = Path.GetPathRoot(dir) ?? string.Empty;
                var (media, bus) = DiskKind(context, root);
                var space = context.FileSystem.GetDriveSpace(root);
                var evidence = $"{dir} — {media}{(bus is null ? string.Empty : $" ({bus})")}" +
                               (space is null ? string.Empty : $", livre {space.FreeFraction:P0}");
                if (media == "HDD" || bus == "USB")
                {
                    yield return GameChecks.Game(Id, $"{game.DisplayName} está em disco lento",
                        "O jogo está em HDD ou disco USB: carregamentos e streaming de texturas ficam bem mais lentos. Mova a " +
                        "instalação para um SSD (Steam/Ubisoft Connect têm opção de mover sem baixar de novo).",
                        Severity.Warning, game.Target, evidence);
                }
                else if (space is not null && space.FreeFraction < 0.10)
                {
                    yield return GameChecks.Game(Id, $"{game.DisplayName}: disco quase cheio",
                        "O disco do jogo tem menos de 10% livre; atualizações e cache de shaders podem falhar ou ficar lentos.",
                        Severity.Warning, game.Target, evidence);
                }
                else
                {
                    yield return GameChecks.Game(Id, $"{game.DisplayName}: disco da instalação",
                        bus == "SATA" ? "SSD SATA: adequado. Um NVMe reduz pouco o carregamento nestes jogos." : "Nada a fazer.",
                        Severity.Info, game.Target, evidence);
                }
            }
        }
    }

    private static (string Media, string? Bus) DiskKind(DiagnosticContext ctx, string root)
    {
        if (root.Length < 2 || !char.IsLetter(root[0]))
        {
            return ("desconhecido", null);
        }

        try
        {
            var letter = char.ToUpperInvariant(root[0]);
            var partition = ctx.Wmi.Query(WmiScopes.Storage, $"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'").FirstOrDefault();
            var disk = partition?.GetUInt64("DiskNumber");
            if (disk is null)
            {
                return ("desconhecido", null);
            }

            var pd = ctx.Wmi.Query(WmiScopes.Storage, $"SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{disk.Value.ToString(CultureInfo.InvariantCulture)}'").FirstOrDefault();
            var media = pd?.GetUInt64("MediaType") switch
            {
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => "desconhecido",
            };
            var bus = pd?.GetUInt64("BusType") switch
            {
                17 => "NVMe",
                11 => "SATA",
                7 => "USB",
                _ => null,
            };
            return (media, bus);
        }
#pragma warning disable CA1031 // Tipo de disco é best-effort (WMI de armazenamento pode não existir).
        catch (Exception)
#pragma warning restore CA1031
        {
            return ("desconhecido", null);
        }
    }
}

/// <summary><c>game-refresh-rate</c>: monitor abaixo da taxa máxima (ex.: ficou em 60 Hz).</summary>
public sealed class GameRefreshRateRule(GameTarget games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-refresh-rate";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var gpu in context.Wmi.Query(WmiScopes.Cimv2, "SELECT Name, CurrentRefreshRate, MaxRefreshRate FROM Win32_VideoController"))
        {
            var current = gpu.GetUInt64("CurrentRefreshRate") ?? 0;
            var max = gpu.GetUInt64("MaxRefreshRate") ?? 0;
            if (current > 0 && max > current && current <= 75 && max > 75)
            {
                yield return GameChecks.Game(Id, "Monitor abaixo da taxa de atualização máxima",
                    "A tela está numa taxa baixa, embora o modo máximo seja maior. Causa comum de \"FPS baixo\" percebido. " +
                    "Ajuste em Configurações > Sistema > Tela > Tela avançada > Escolher uma taxa de atualização. O WinTune não altera o modo de vídeo.",
                    Severity.Warning, games, $"Atual: {current} Hz; máximo informado: {max} Hz");
            }
        }
    }
}

/// <summary><c>game-ram-headroom</c>: RAM disponível abaixo do limiar (padrão 6 GB) no momento da análise.</summary>
public sealed class GameRamHeadroomRule(GameTarget games, ulong thresholdBytes = 6UL * 1024 * 1024 * 1024) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-ram-headroom";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var os = context.Wmi.Query(WmiScopes.Cimv2, "SELECT FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
        var freeKb = os?.GetUInt64("FreePhysicalMemory");
        if (freeKb is null)
        {
            yield break;
        }

        var free = freeKb.Value * 1024;
        if (free < thresholdBytes)
        {
            yield return GameChecks.Game(Id, "Pouca RAM disponível para o jogo",
                "Há menos memória livre do que o recomendado para jogar sem paginação. Feche navegador/launchers pesados antes de jogar. " +
                "A aba Telemetria mostra quem consome durante a partida. O WinTune nunca finaliza processos.",
                Severity.Warning, games, $"Disponível: {GameChecks.Size((long)free)} (limiar {GameChecks.Size((long)thresholdBytes)})");
        }
    }
}

/// <summary><c>game-shader-cache-state</c>: tamanho dos caches de shader (recomenda não apagar).</summary>
public sealed class GameShaderCacheStateRule(GameTarget games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-shader-cache-state";

    /// <summary>Pastas de cache de shader conhecidas (relativas a LocalAppData; "Low" = LocalLow).</summary>
    public static IReadOnlyList<string> CacheFolders(IEnvironmentInfo env)
    {
        ArgumentNullException.ThrowIfNull(env);
        var local = env.GetFolder(KnownFolder.LocalAppData);
        return
        [
            Path.Combine(local, "D3DSCache"),
            Path.Combine(local, "NVIDIA", "DXCache"),
            Path.Combine(local, "NVIDIA", "GLCache"),
            Path.Combine(local + "Low", "NVIDIA", "PerDriverVersion", "DXCache"),
        ];
    }

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var parts = new List<string>();
        long total = 0;
        foreach (var dir in CacheFolders(context.Environment).Where(context.FileSystem.DirectoryExists))
        {
            var size = context.FileSystem.EnumerateFiles(dir, "*", recursive: true).Sum(f => f.Length);
            total += size;
            parts.Add($"{dir}: {GameChecks.Size(size)}");
        }

        yield return GameChecks.Game(Id, "Cache de shaders",
            "Os caches de shader evitam recompilar shaders a cada partida. Não apague, a menos que haja bug visual: depois de limpar, " +
            "os primeiros carregamentos ficam mais lentos e pode haver travadas enquanto recompila.",
            Severity.Info, games, parts.Count == 0 ? "Nenhuma pasta de cache encontrada" : $"Total {GameChecks.Size(total)} — " + string.Join("; ", parts));
    }
}

/// <summary><c>game-gpu-pref-high</c> como guia (formato <c>GpuPreference=N</c> sem tabela oficial, V9).</summary>
public sealed class GameGpuPreferenceGuide(GameTarget games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-gpu-pref-high";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Profile.GpuCount > 1)
        {
            yield return GameChecks.Game(Id, "Escolher a GPU dedicada para o jogo",
                "Há mais de uma GPU. Em Configurações > Sistema > Tela > Elementos gráficos, adicione o executável do jogo e escolha " +
                "\"Alto desempenho\" (GPU dedicada). O WinTune não grava essa preferência sozinho porque o significado dos valores não " +
                "tem documentação oficial.",
                Severity.Info, games, string.Join(", ", context.Profile.GpuNames));
        }
    }
}

/// <summary><c>game-fso-compat</c> como guia experimental (valor sem documentação oficial, V18).</summary>
public sealed class GameFsoCompatGuide(GameTarget games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-fso-compat";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        yield return GameChecks.Game(Id, "Otimizações de tela inteira (experimental)",
            "Benefício controverso. Só teste se o benchmark mostrar ganho: clique com o botão direito no executável do jogo > Propriedades > " +
            "Compatibilidade > \"Desabilitar otimizações de tela inteira\". Meça antes e depois e desfaça se não houver diferença.",
            Severity.Info, games);
    }
}

/// <summary><c>game-guide-nvcp</c>: Painel de Controle NVIDIA por programa.</summary>
public sealed class GameNvcpGuide(GameTarget games) : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-guide-nvcp";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Profile.HasNvidiaGpu)
        {
            yield break;
        }

        yield return GameChecks.Game(Id, "Painel de Controle NVIDIA (por programa)",
            "Em Gerenciar configurações 3D > Configurações de programa, para o jogo: \"Modo de baixa latência\" (se o jogo não tiver " +
            "NVIDIA Reflex) e \"Modo de gerenciamento de energia: Preferir desempenho máximo\". Se o jogo tiver Reflex, prefira o Reflex. " +
            "O WinTune não grava perfis de driver.",
            Severity.Info, games);
    }
}

/// <summary><c>game-guide-siege</c>: checklist textual do Siege.</summary>
public sealed class GameGuideSiege : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-guide-siege";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        yield return GameChecks.Game(Id, "Rainbow Six Siege: checklist no jogo",
            "1) Limite de FPS um pouco abaixo da taxa do monitor (ou desligado, se preferir latência mínima com Reflex). " +
            "2) V-Sync desligado com monitor VRR. 3) NVIDIA Reflex ligado, se disponível. 4) Escala de resolução em 100% (ou DLSS, se houver). " +
            "5) API gráfica: compare as opções que o jogo oferecer hoje (DX11/DX12) usando o Benchmark do WinTune; não há uma melhor para todos. " +
            "O jogo é sensível a CPU e memória: EXPO/XMP e dual channel costumam pesar mais que qualquer ajuste. Nomes de menu podem mudar entre temporadas.",
            Severity.Info, GameTarget.RainbowSixSiege);
    }
}

/// <summary><c>game-guide-cs2</c>: checklist textual do CS2 (sem inventar flags).</summary>
public sealed class GameGuideCs2 : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "game-guide-cs2";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        yield return GameChecks.Game(Id, "Counter-Strike 2: checklist no jogo",
            "1) NVIDIA Reflex ligado. 2) Limite de FPS (opção do jogo) estável, sem oscilação. 3) Modo de janela: compare tela cheia e " +
            "janela sem borda com o Benchmark. 4) MSAA: comece baixo e suba se sobrar desempenho. 5) autoexec.cfg e opções de " +
            "inicialização da Steam: use apenas comandos documentados pela Valve ou pela wiki oficial do jogo; o WinTune não sugere flags " +
            "nem edita arquivos do jogo. O CS2 depende muito de CPU e cache: o 3D V-Cache tende a ajudar (confirme com a telemetria).",
            Severity.Info, GameTarget.Cs2);
    }
}
