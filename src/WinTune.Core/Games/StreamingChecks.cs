using System.Globalization;
using WinTune.Core.Diagnostics;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Games;

/// <summary>
/// Verificações (Check) e guias (Guia) de livestreaming. Somente leitura: nada altera o sistema, nada toca no jogo ou no OBS
/// e nenhum dado é enviado pela rede (a velocidade de upload NÃO é medida, porque isso exigiria tráfego de rede).
/// </summary>
public static class StreamingChecks
{
    /// <summary>Espaço livre mínimo recomendado para gravar (GB).</summary>
    public const double MinRecordingFreeGb = 50;

    /// <summary>Regras da aba Live.</summary>
    public static IReadOnlyList<IDiagnosticRule> Rules() =>
    [
        new StreamingNetworkRule(),
        new StreamingRecordingSpaceRule(),
        new StreamingEncoderGuide(),
        new StreamingObsGuide(),
        new StreamingSessionGuide(),
    ];

    internal static Finding Live(string id, string title, string message, Severity severity, string? evidence = null) =>
        new(id, title, message, severity, evidence, GameTarget.None, FindingCategory.Streaming);
}

/// <summary><c>streaming-network</c>: conexão por Wi-Fi ou cabo lento (leitura do adaptador; não mede upload).</summary>
public sealed class StreamingNetworkRule : IDiagnosticRule
{
    private static readonly string[] WirelessMarkers = ["wi-fi", "wifi", "wireless", "802.11", "wlan"];
    private static readonly string[] IgnoredMarkers = ["bluetooth", "virtual", "vpn", "tap-", "tunnel", "loopback"];

    /// <inheritdoc />
    public string Id => "streaming-network";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var adapters = context.Wmi
            .Query(WmiScopes.Cimv2, "SELECT Name, NetConnectionStatus, Speed, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .Where(r => r.GetUInt64("NetConnectionStatus") == 2)
            .Select(r => (Name: r.GetString("Name") ?? string.Empty, Speed: r.GetUInt64("Speed") ?? 0))
            .Where(a => !IgnoredMarkers.Any(m => a.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (adapters.Count == 0)
        {
            yield break;
        }

        var wired = adapters.Where(a => !WirelessMarkers.Any(m => a.Name.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
        var evidence = string.Join("; ", adapters.Select(a => $"{a.Name}: {Mbps(a.Speed)}"));
        if (wired.Count == 0)
        {
            yield return StreamingChecks.Live(Id, "Você está transmitindo por Wi-Fi",
                "Wi-Fi costuma oscilar (latência e perda de pacotes), o que causa quedas de quadros na live mesmo com upload alto. " +
                "Prefira cabo de rede. O WinTune não mede a velocidade de upload; teste no próprio serviço de transmissão.",
                Severity.Warning, evidence);
        }
        else if (wired.All(a => a.Speed > 0 && a.Speed < 1_000_000_000UL))
        {
            yield return StreamingChecks.Live(Id, "Cabo de rede abaixo de 1 Gb/s",
                "O adaptador cabeado negociou menos de 1 Gb/s. Confira o cabo, a porta do roteador e o modo de negociação do adaptador. " +
                "Para a maioria das lives o upload da operadora é o limite real, não o cabo.",
                Severity.Info, evidence);
        }
        else
        {
            yield return StreamingChecks.Live(Id, "Conexão cabeada",
                "Boa base para transmitir. Lembre que o limite costuma ser o upload da operadora, que o WinTune não mede.",
                Severity.Info, evidence);
        }
    }

    private static string Mbps(ulong bitsPerSecond) =>
        bitsPerSecond == 0 ? "velocidade desconhecida" : (bitsPerSecond / 1_000_000d).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " Mb/s";
}

/// <summary><c>streaming-recording-space</c>: espaço livre para gravar.</summary>
public sealed class StreamingRecordingSpaceRule : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "streaming-recording-space";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var space = context.FileSystem.GetDriveSpace(context.Environment.SystemDrive);
        if (space is null)
        {
            yield break;
        }

        var freeGb = space.FreeBytes / 1024d / 1024d / 1024d;
        if (freeGb < StreamingChecks.MinRecordingFreeGb)
        {
            yield return StreamingChecks.Live(Id, "Pouco espaço para gravar no disco do sistema",
                "Gravar em alta qualidade enche o disco rápido: uma hora a 50 Mb/s ocupa cerca de 22 GB. O OBS costuma gravar na pasta " +
                "Vídeos do usuário; grave em outro SSD com espaço, ou reduza o bitrate da gravação.",
                Severity.Warning, string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"Livres no disco do sistema: {freeGb:0.#} GB"));
        }
    }
}

/// <summary><c>streaming-encoder-guide</c>: qual codificador usar.</summary>
public sealed class StreamingEncoderGuide : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "streaming-encoder-guide";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Profile.HasNvidiaGpu)
        {
            yield return StreamingChecks.Live(Id, "Codificador: prefira o NVENC da placa de vídeo",
                "Codificar pela CPU (x264) disputa núcleos com o jogo, e Siege e CS2 dependem muito de CPU. O NVENC usa um bloco dedicado da GPU " +
                "NVIDIA e poupa a CPU. No OBS, em Saída, escolha o codificador NVIDIA NVENC. O AV1 só vale se a plataforma de destino aceitar. " +
                "Nomes de menu mudam entre versões do OBS: confira na sua. Para ver se o codificador está sobrecarregado, use a aba Telemetria.",
                Severity.Info, string.Join(", ", context.Profile.GpuNames));
        }
        else
        {
            yield return StreamingChecks.Live(Id, "Codificador: prefira o de hardware da sua GPU",
                "Se a sua GPU tem codificador de hardware (AMD AMF, Intel Quick Sync), use-o no OBS em vez de codificar pela CPU, para " +
                "poupar núcleos para o jogo. Sem codificador de hardware, o x264 em preset rápido é a alternativa; meça o impacto na aba Telemetria.",
                Severity.Info, context.Profile.GpuNames.Count == 0 ? null : string.Join(", ", context.Profile.GpuNames));
        }
    }
}

/// <summary><c>streaming-guide-obs</c>: checklist do OBS (sem inventar valores).</summary>
public sealed class StreamingObsGuide : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "streaming-guide-obs";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        yield return StreamingChecks.Live(Id, "OBS: checklist",
            "1) Bitrate de saída com folga em relação ao upload da sua conexão e dentro do limite da plataforma (veja a documentação " +
            "oficial dela). 2) Captura de Jogo costuma pesar menos que Captura de Tela; use a de tela só quando a de jogo não funcionar. " +
            "3) Resolução e FPS de saída menores que os do jogo aliviam o codificador. 4) Poucas fontes de navegador e filtros pesados. " +
            "5) Grave e transmita em discos/redes diferentes quando possível. 6) Faça uma gravação de teste e confira quadros perdidos nas " +
            "estatísticas do OBS. O WinTune não altera configurações do OBS.",
            Severity.Info);
    }
}

/// <summary><c>streaming-guide-session</c>: como medir o impacto da live.</summary>
public sealed class StreamingSessionGuide : IDiagnosticRule
{
    /// <inheritdoc />
    public string Id => "streaming-guide-session";

    /// <inheritdoc />
    public IEnumerable<Finding> Evaluate(DiagnosticContext context)
    {
        yield return StreamingChecks.Live(Id, "Meça jogando com a live ligada",
            "Rode uma sessão de diagnóstico na aba Telemetria com o OBS transmitindo. Ela mostra se o limite é GPU, CPU, memória ou o " +
            "codificador. No Benchmark, compare rodadas com e sem a live (mesmo mapa/cenário) para saber o custo real da transmissão no seu FPS.",
            Severity.Info);
    }
}
