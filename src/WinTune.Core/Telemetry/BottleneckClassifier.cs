using System.Globalization;
using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Telemetry;

/// <summary>Componente que limita o desempenho.</summary>
public enum Bottleneck
{
    /// <summary>Dados insuficientes.</summary>
    InsufficientData,

    /// <summary>Sem gargalo claro.</summary>
    None,

    /// <summary>Limitado pela GPU (normal e desejável em jogos).</summary>
    Gpu,

    /// <summary>Limitado por uma thread da CPU.</summary>
    CpuSingleThread,

    /// <summary>CPU inteira saturada.</summary>
    CpuMultiThread,

    /// <summary>Memória RAM insuficiente (paginação).</summary>
    Ram,

    /// <summary>Disco saturado.</summary>
    Disk,
}

/// <summary>Resultado da classificação.</summary>
/// <param name="Primary">Gargalo principal.</param>
/// <param name="Summary">Resumo pt-BR.</param>
/// <param name="Evidence">Evidências medidas.</param>
/// <param name="Findings">Achados (ex.: <c>thermal-throttle</c>).</param>
public sealed record BottleneckReport(Bottleneck Primary, string Summary, IReadOnlyList<string> Evidence, IReadOnlyList<Finding> Findings);

/// <summary>Regras de classificação sobre uma janela de amostras (Fase 7).</summary>
public sealed class BottleneckClassifier
{
    /// <summary>Mínimo de amostras para concluir algo.</summary>
    public const int MinSamples = 10;

    /// <summary>Fração da janela para considerar "sustentado".</summary>
    public const double Sustained = 0.5;

    /// <summary>Páginas/s consideradas paginação alta.</summary>
    public const double HighPaging = 1000;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Classifica a janela.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "API de instância mantida para limiares configuráveis.")]
    public BottleneckReport Classify(IReadOnlyList<TelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < MinSamples)
        {
            return new BottleneckReport(Bottleneck.InsufficientData,
                $"Dados insuficientes: são necessárias pelo menos {MinSamples} amostras (≈{MinSamples} s).", [], []);
        }

        var r = samples.Select(s => s.Reading).ToList();
        var evidence = new List<string>();
        var findings = new List<Finding>();

        double Frac(Func<SensorReading, bool> predicate) => r.Count(predicate) / (double)r.Count;

        var ram = Frac(x => x.RamUsedPercent > 90 && x.PagesPerSecond > HighPaging);
        var gpu = Frac(x => x.GpuUtilizationPercent >= 95);
        var cpuSingle = Frac(x => x.CpuMaxThreadPercent >= 90 && (x.GpuUtilizationPercent is null || x.GpuUtilizationPercent < 85));
        var cpuMulti = Frac(x => x.CpuTotalPercent >= 90);
        var disk = Frac(x => x.DiskBusyPercent >= 90);

        Add(evidence, "GPU ≥ 95%", gpu);
        Add(evidence, "1 thread de CPU ≥ 90% com GPU < 85%", cpuSingle);
        Add(evidence, "CPU total ≥ 90%", cpuMulti);
        Add(evidence, "RAM > 90% com paginação alta", ram);
        Add(evidence, "Disco ≥ 90% ocupado", disk);

        var thermal = DetectThrottling(r, evidence);
        if (thermal is not null)
        {
            findings.Add(thermal);
        }

        // Livestream/gravação: codificador de vídeo da GPU saturado derruba quadros na transmissão mesmo que o jogo esteja bem.
        var encoder = Frac(x => x.GpuEncoderPercent >= 90);
        Add(evidence, "Codificador de vídeo da GPU ≥ 90%", encoder);
        if (encoder >= 0.3)
        {
            findings.Add(new Finding("streaming-encoder-saturated", "Codificador de vídeo da GPU saturado",
                "O codificador (NVENC ou equivalente) passou boa parte da sessão perto do limite. Na live isso causa quadros perdidos ou " +
                "travadas na transmissão. Reduza a resolução ou o FPS da saída, use um preset de codificação mais leve no OBS ou " +
                "desligue gravações paralelas (inclusive a da Xbox Game Bar).",
                Severity.Warning, string.Create(PtBr, $"Codificador ≥ 90% em {encoder:P0} do tempo")));
        }

        Bottleneck primary;
        if (ram >= 0.3)
        {
            primary = Bottleneck.Ram;
        }
        else if (gpu >= Sustained)
        {
            primary = Bottleneck.Gpu;
        }
        else if (cpuSingle >= Sustained)
        {
            primary = Bottleneck.CpuSingleThread;
        }
        else if (cpuMulti >= Sustained)
        {
            primary = Bottleneck.CpuMultiThread;
        }
        else if (disk >= Sustained)
        {
            primary = Bottleneck.Disk;
        }
        else
        {
            primary = Bottleneck.None;
        }

        var summary = $"Durante esta sessão: gargalo principal = {Describe(primary)}." +
                      (thermal is null ? string.Empty : " Também houve sinal de limitação térmica/energia.");
        return new BottleneckReport(primary, summary, evidence, findings);
    }

    /// <summary>Descrição pt-BR do gargalo.</summary>
    public static string Describe(Bottleneck b) => b switch
    {
        Bottleneck.Gpu => "GPU (normal em jogos; a placa de vídeo está sendo bem usada)",
        Bottleneck.CpuSingleThread => "CPU, uma thread (o jogo espera a CPU; memória rápida e menos apps em segundo plano ajudam)",
        Bottleneck.CpuMultiThread => "CPU inteira saturada (feche apps em segundo plano)",
        Bottleneck.Ram => "memória RAM (o Windows está paginando; feche apps ou considere mais RAM)",
        Bottleneck.Disk => "disco (o armazenamento está saturado)",
        Bottleneck.None => "nenhum componente claramente saturado",
        _ => "dados insuficientes",
    };

    private static Finding? DetectThrottling(IReadOnlyList<SensorReading> r, List<string> evidence)
    {
        var third = r.Count / 3;
        if (third < 3)
        {
            return null;
        }

        // GPU sob carga: clock caindo entre o primeiro e o último terço com temperatura alta.
        var gpuLoaded = r.Where(x => x.GpuUtilizationPercent >= 80 && x.GpuClockMhz is not null).ToList();
        if (gpuLoaded.Count >= 6)
        {
            var t = gpuLoaded.Count / 3;
            var early = Median(gpuLoaded.Take(t).Select(x => x.GpuClockMhz!.Value));
            var late = Median(gpuLoaded.TakeLast(t).Select(x => x.GpuClockMhz!.Value));
            var maxTemp = gpuLoaded.Max(x => x.GpuTemperatureC ?? 0);
            if (early > 0 && (early - late) / early > 0.10 && maxTemp >= 83)
            {
                evidence.Add(string.Create(PtBr, $"Clock da GPU caiu de {early:0} para {late:0} MHz com {maxTemp:0} °C"));
                return new Finding("thermal-throttle", "Possível limitação térmica da GPU",
                    "O clock da GPU caiu sob carga enquanto a temperatura subia. Revise o fluxo de ar do gabinete, a limpeza dos filtros " +
                    "e, se a placa for antiga, a pasta térmica. O WinTune não altera curvas de ventoinha.",
                    Severity.Warning, string.Create(PtBr, $"{early:0} → {late:0} MHz, máx. {maxTemp:0} °C"));
            }
        }

        // CPU sob carga: clock efetivo caindo.
        var cpuLoaded = r.Where(x => x.CpuTotalPercent >= 80 && x.CpuEffectiveClockMhz is not null).ToList();
        if (cpuLoaded.Count >= 6)
        {
            var t = cpuLoaded.Count / 3;
            var early = Median(cpuLoaded.Take(t).Select(x => x.CpuEffectiveClockMhz!.Value));
            var late = Median(cpuLoaded.TakeLast(t).Select(x => x.CpuEffectiveClockMhz!.Value));
            if (early > 0 && (early - late) / early > 0.15)
            {
                var temp = cpuLoaded.Max(x => x.CpuTemperatureC);
                evidence.Add(string.Create(PtBr, $"Clock efetivo da CPU caiu de {early:0} para {late:0} MHz"));
                return new Finding("thermal-throttle", "Possível limitação térmica/energia da CPU",
                    "O clock efetivo da CPU caiu sob carga sustentada. Verifique o cooler, o fluxo de ar e o plano de energia. " +
                    "A temperatura exibida vem da zona térmica ACPI e é aproximada.",
                    Severity.Warning, string.Create(PtBr, $"{early:0} → {late:0} MHz{(temp is null ? string.Empty : $", zona térmica máx. {temp:0} °C")}"));
            }
        }

        return null;
    }

    private static void Add(List<string> evidence, string label, double fraction)
    {
        if (fraction > 0)
        {
            evidence.Add(string.Create(PtBr, $"{label}: {fraction:P0} do tempo"));
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0)
        {
            return 0;
        }

        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[(v.Count / 2) - 1] + v[v.Count / 2]) / 2;
    }
}
