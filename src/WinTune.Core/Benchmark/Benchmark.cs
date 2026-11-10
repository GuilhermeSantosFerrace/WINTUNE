using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinTune.Core.Telemetry;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Benchmark;

/// <summary>Estatísticas de frametime (só existem com fonte confiável, ex.: CSV do PresentMon).</summary>
public sealed record FrameStats
{
    /// <summary>FPS médio (1000 / frametime médio).</summary>
    public double AverageFps { get; init; }

    /// <summary>1% low (1000 / percentil 99 do frametime).</summary>
    public double OnePercentLowFps { get; init; }

    /// <summary>Desvio padrão do frametime (ms).</summary>
    public double FrameTimeStdDevMs { get; init; }

    /// <summary>Quadros considerados.</summary>
    public int FrameCount { get; init; }

    /// <summary>Origem (ex.: "PresentMon").</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Calcula a partir de frametimes (ms).</summary>
    public static FrameStats? From(IReadOnlyList<double> frameTimesMs, string source)
    {
        ArgumentNullException.ThrowIfNull(frameTimesMs);
        var ft = frameTimesMs.Where(v => v > 0 && v < 1000).ToList();
        if (ft.Count < 100)
        {
            return null;
        }

        var mean = ft.Average();
        var sd = Math.Sqrt(ft.Sum(v => (v - mean) * (v - mean)) / (ft.Count - 1));
        var sorted = ft.OrderBy(v => v).ToList();
        var p99 = sorted[(int)Math.Ceiling(0.99 * sorted.Count) - 1];
        return new FrameStats
        {
            AverageFps = 1000.0 / mean,
            OnePercentLowFps = 1000.0 / p99,
            FrameTimeStdDevMs = sd,
            FrameCount = ft.Count,
            Source = source,
        };
    }
}

/// <summary>Resumo da telemetria de uma rodada.</summary>
public sealed record TelemetrySummary
{
    /// <summary>Amostras.</summary>
    public int SampleCount { get; init; }

    /// <summary>CPU média (%).</summary>
    public double? CpuAveragePercent { get; init; }

    /// <summary>GPU média (%).</summary>
    public double? GpuAveragePercent { get; init; }

    /// <summary>Temperatura máxima da GPU (°C).</summary>
    public double? GpuMaxTemperatureC { get; init; }

    /// <summary>Temperatura máxima (zona térmica) (°C).</summary>
    public double? CpuMaxTemperatureC { get; init; }

    /// <summary>Uso máximo de RAM (%).</summary>
    public double? RamMaxUsedPercent { get; init; }

    /// <summary>Paginação média (páginas/s).</summary>
    public double? PagesPerSecondAverage { get; init; }

    /// <summary>Calcula a partir das amostras.</summary>
    public static TelemetrySummary From(IReadOnlyList<TelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var r = samples.Select(s => s.Reading).ToList();
        static double? Avg(IEnumerable<double?> v)
        {
            var l = v.OfType<double>().ToList();
            return l.Count == 0 ? null : l.Average();
        }

        static double? Max(IEnumerable<double?> v)
        {
            var l = v.OfType<double>().ToList();
            return l.Count == 0 ? null : l.Max();
        }

        return new TelemetrySummary
        {
            SampleCount = r.Count,
            CpuAveragePercent = Avg(r.Select(x => x.CpuTotalPercent)),
            GpuAveragePercent = Avg(r.Select(x => x.GpuUtilizationPercent)),
            GpuMaxTemperatureC = Max(r.Select(x => x.GpuTemperatureC)),
            CpuMaxTemperatureC = Max(r.Select(x => x.CpuTemperatureC)),
            RamMaxUsedPercent = Max(r.Select(x => x.RamUsedPercent)),
            PagesPerSecondAverage = Avg(r.Select(x => x.PagesPerSecond)),
        };
    }
}

/// <summary>Uma rodada de medição.</summary>
public sealed record BenchmarkRun
{
    /// <summary>Versão do schema.</summary>
    public int Schema { get; init; } = 1;

    /// <summary>ID.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Jogo/atividade (<c>siege</c>, <c>cs2</c>, <c>geral</c>).</summary>
    public required string Game { get; init; }

    /// <summary>Condição (ex.: <c>antes</c>, <c>depois</c>).</summary>
    public required string Condition { get; init; }

    /// <summary>Início.</summary>
    public DateTimeOffset StartedUtc { get; init; }

    /// <summary>Duração.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Telemetria.</summary>
    public required TelemetrySummary Telemetry { get; init; }

    /// <summary>Frametime (null = sem fonte confiável; nenhum FPS é inventado).</summary>
    public FrameStats? Frames { get; init; }
}

/// <summary>Leitura de CSV do PresentMon (ferramenta externa opcional). Suporta 1.x (<c>MsBetweenPresents</c>) e 2.x (<c>FrameTime</c>).</summary>
public static partial class PresentMonCsv
{
    /// <summary>Tamanho máximo aceito.</summary>
    public const long MaxBytes = 200L * 1024 * 1024;

    private static readonly string[] FrameTimeColumns = ["MsBetweenPresents", "FrameTime", "msBetweenPresents"];

    /// <summary>Extrai frametimes (ms); filtra pelo processo se informado.</summary>
    public static IReadOnlyList<double> Parse(string csv, string? processName = null)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var lines = csv.Split('\n');
        var headerIndex = Array.FindIndex(lines, l => FrameTimeColumns.Any(c => l.Contains(c, StringComparison.Ordinal)));
        if (headerIndex < 0)
        {
            throw new InvalidDataException("CSV sem coluna de frametime (MsBetweenPresents/FrameTime). É um arquivo do PresentMon?");
        }

        var header = Split(lines[headerIndex]);
        var ftCol = FrameTimeColumns.Select(c => Array.IndexOf(header, c)).FirstOrDefault(i => i >= 0, -1);
        var appCol = Array.IndexOf(header, "Application");
        var result = new List<double>();
        foreach (var raw in lines.Skip(headerIndex + 1))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var cells = Split(line);
            if (ftCol >= cells.Length)
            {
                continue;
            }

            if (processName is not null && appCol >= 0 && appCol < cells.Length &&
                !cells[appCol].Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (double.TryParse(cells[ftCol], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                result.Add(v);
            }
        }

        return result;
    }

    private static string[] Split(string line) => line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
}

/// <summary>Veredito de uma métrica.</summary>
public enum Verdict
{
    /// <summary>Melhorou.</summary>
    Improvement,

    /// <summary>Piorou.</summary>
    Regression,

    /// <summary>Sem diferença estatisticamente relevante.</summary>
    NoSignificantDifference,

    /// <summary>Dados insuficientes.</summary>
    InsufficientData,
}

/// <summary>Comparação de uma métrica.</summary>
/// <param name="Metric">Nome.</param>
/// <param name="Before">Média antes.</param>
/// <param name="After">Média depois.</param>
/// <param name="ChangePercent">Variação (%).</param>
/// <param name="Noise">Variação entre rodadas (2 erros padrão).</param>
/// <param name="Verdict">Veredito.</param>
public sealed record MetricComparison(string Metric, double Before, double After, double ChangePercent, double Noise, Verdict Verdict);

/// <summary>Relatório antes/depois de um jogo.</summary>
/// <param name="Game">Jogo.</param>
/// <param name="Overall">Veredito geral.</param>
/// <param name="Metrics">Métricas.</param>
/// <param name="Notes">Observações.</param>
public sealed record ComparisonReport(string Game, Verdict Overall, IReadOnlyList<MetricComparison> Metrics, IReadOnlyList<string> Notes);

/// <summary>Comparador antes/depois. Nunca mistura jogos; descarta a 1ª rodada de cada condição (cache de shader).</summary>
public sealed class Comparator
{
    /// <summary>Rodadas mínimas por condição (R7).</summary>
    public const int MinRunsPerCondition = 3;

    /// <summary>Limiar mínimo de variação relativa (padrão 3%).</summary>
    public double Threshold { get; init; } = 0.03;

    /// <summary>Compara as rodadas.</summary>
    public ComparisonReport Compare(IReadOnlyList<BenchmarkRun> before, IReadOnlyList<BenchmarkRun> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var games = before.Concat(after).Select(r => r.Game).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (games.Count > 1)
        {
            return new ComparisonReport(string.Join("+", games), Verdict.InsufficientData, [],
                ["As rodadas são de jogos diferentes; o relatório nunca mistura jogos. Compare um jogo por vez."]);
        }

        var game = games.FirstOrDefault() ?? "?";
        var notes = new List<string>();
        if (before.Count < MinRunsPerCondition || after.Count < MinRunsPerCondition)
        {
            return new ComparisonReport(game, Verdict.InsufficientData, [],
                [$"São necessárias pelo menos {MinRunsPerCondition} rodadas por condição (antes: {before.Count}, depois: {after.Count}), no mesmo mapa/cenário."]);
        }

        var a = before.OrderBy(r => r.StartedUtc).Skip(1).ToList();
        var b = after.OrderBy(r => r.StartedUtc).Skip(1).ToList();
        notes.Add("A 1ª rodada de cada condição foi descartada (compilação de shaders/cache).");

        var metrics = new List<MetricComparison>();
        var hasFrames = a.Concat(b).All(r => r.Frames is not null);
        if (hasFrames)
        {
            metrics.Add(Metric("FPS médio", a, b, r => r.Frames!.AverageFps, higherIsBetter: true));
            metrics.Add(Metric("1% low (FPS)", a, b, r => r.Frames!.OnePercentLowFps, higherIsBetter: true));
            metrics.Add(Metric("Desvio do frametime (ms)", a, b, r => r.Frames!.FrameTimeStdDevMs, higherIsBetter: false));
        }
        else
        {
            notes.Add("Sem dados de frametime em todas as rodadas: mostrando só telemetria. Nenhum FPS é estimado.");
        }

        AddIfAll(metrics, "Temperatura máx. da GPU (°C)", a, b, r => r.Telemetry.GpuMaxTemperatureC, higherIsBetter: false);
        AddIfAll(metrics, "Uso máx. de RAM (%)", a, b, r => r.Telemetry.RamMaxUsedPercent, higherIsBetter: false);
        AddIfAll(metrics, "Paginação média (páginas/s)", a, b, r => r.Telemetry.PagesPerSecondAverage, higherIsBetter: false);
        AddIfAll(metrics, "Uso médio da GPU (%)", a, b, r => r.Telemetry.GpuAveragePercent, higherIsBetter: true);

        Verdict overall;
        if (hasFrames)
        {
            var fps = metrics[0].Verdict;
            var low = metrics[1].Verdict;
            overall = fps == Verdict.Regression || low == Verdict.Regression ? Verdict.Regression
                : fps == Verdict.Improvement || low == Verdict.Improvement ? Verdict.Improvement
                : Verdict.NoSignificantDifference;
        }
        else
        {
            overall = Verdict.InsufficientData;
        }

        return new ComparisonReport(game, overall, metrics, notes);
    }

    private void AddIfAll(List<MetricComparison> metrics, string name, List<BenchmarkRun> a, List<BenchmarkRun> b, Func<BenchmarkRun, double?> f, bool higherIsBetter)
    {
        if (a.Concat(b).All(r => f(r) is not null))
        {
            metrics.Add(Metric(name, a, b, r => f(r)!.Value, higherIsBetter));
        }
    }

    private MetricComparison Metric(string name, List<BenchmarkRun> a, List<BenchmarkRun> b, Func<BenchmarkRun, double> f, bool higherIsBetter)
    {
        var va = a.Select(f).ToList();
        var vb = b.Select(f).ToList();
        var ma = va.Average();
        var mb = vb.Average();
        var noise = 2 * Math.Sqrt((Variance(va) / va.Count) + (Variance(vb) / vb.Count));
        var change = ma == 0 ? 0 : (mb - ma) / Math.Abs(ma);
        var diff = mb - ma;
        Verdict verdict;
        if (Math.Abs(change) < Threshold || Math.Abs(diff) <= noise)
        {
            verdict = Verdict.NoSignificantDifference;
        }
        else
        {
            verdict = (diff > 0) == higherIsBetter ? Verdict.Improvement : Verdict.Regression;
        }

        return new MetricComparison(name, ma, mb, change * 100, noise, verdict);
    }

    private static double Variance(List<double> v)
    {
        if (v.Count < 2)
        {
            return 0;
        }

        var m = v.Average();
        return v.Sum(x => (x - m) * (x - m)) / (v.Count - 1);
    }
}

/// <summary>Persistência das rodadas em <c>%LOCALAPPDATA%\WinTune\sessions\*.json</c>.</summary>
public sealed partial class BenchmarkStore
{
    /// <summary>Tamanho máximo de um arquivo de sessão.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly IFileSystem _fs;

    /// <summary>Cria o repositório.</summary>
    public BenchmarkStore(IFileSystem fs, IEnvironmentInfo env)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        Directory = Path.Combine((env ?? throw new ArgumentNullException(nameof(env))).GetFolder(KnownFolder.LocalAppData), "WinTune", "sessions");
    }

    /// <summary>Pasta das sessões.</summary>
    public string Directory { get; }

    /// <summary>Grava uma rodada.</summary>
    public string Save(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var name = $"{run.StartedUtc:yyyyMMdd-HHmmss}-{Safe(run.Game)}-{Safe(run.Condition)}-{run.Id[..8]}.json";
        var path = Path.Combine(Directory, name);
        _fs.CreateDirectory(Directory);
        _fs.WriteAllTextAtomic(path, JsonSerializer.Serialize(run, Json));
        return path;
    }

    /// <summary>Carrega as rodadas válidas (arquivos inválidos são ignorados e listados em <paramref name="errors"/>).</summary>
    public IReadOnlyList<BenchmarkRun> LoadAll(out IReadOnlyList<string> errors)
    {
        var list = new List<BenchmarkRun>();
        var errs = new List<string>();
        if (_fs.DirectoryExists(Directory))
        {
            foreach (var f in _fs.EnumerateFiles(Directory, "*.json", recursive: false))
            {
                try
                {
                    if (f.Length > MaxBytes || f.IsReparsePoint)
                    {
                        throw new InvalidDataException("arquivo grande demais ou link");
                    }

                    var run = JsonSerializer.Deserialize<BenchmarkRun>(_fs.ReadAllText(f.FullPath), Json)
                              ?? throw new InvalidDataException("vazio");
                    if (run.Schema != 1 || string.IsNullOrWhiteSpace(run.Game) || run.Telemetry is null)
                    {
                        throw new InvalidDataException("schema inválido");
                    }

                    list.Add(run);
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or NotSupportedException)
                {
                    errs.Add($"{Path.GetFileName(f.FullPath)}: {ex.Message}");
                }
            }
        }

        errors = errs;
        return list.OrderBy(r => r.StartedUtc).ToList();
    }

    /// <summary>Apaga uma rodada pelo ID.</summary>
    public void Delete(string runId)
    {
        foreach (var f in _fs.EnumerateFiles(Directory, $"*-{Safe(runId)[..Math.Min(8, runId.Length)]}.json", recursive: false))
        {
            _fs.DeleteFile(f.FullPath);
        }
    }

    private static string Safe(string s) => SafeRegex().Replace(s, "_");

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex SafeRegex();
}
