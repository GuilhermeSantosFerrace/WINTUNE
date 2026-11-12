using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Backup;
using WinTune.Core.Benchmark;
using WinTune.Core.Reports;
using WinTune.Platform.Abstractions;

namespace WinTune.Presentation.ViewModels;

/// <summary>Linha do histórico.</summary>
public sealed class HistoryItem
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public HistoryItem(JournalEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        When = e.TimestampUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", PtBr);
        Action = e.Action;
        Item = e.TweakId;
        Result = e.Result;
        Message = e.Message ?? string.Empty;
    }

    public string When { get; }

    public string Action { get; }

    public string Item { get; }

    public string Result { get; }

    public string Message { get; }
}

/// <summary>Aba Histórico: journal do backup, exportação de relatório e pasta de logs.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;
    private readonly MainViewModel _main;

    public HistoryViewModel(AppServices services, IDialogService dialogs, Action<string> log, MainViewModel main)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _main = main ?? throw new ArgumentNullException(nameof(main));
    }

    public ObservableCollection<HistoryItem> Entries { get; } = [];

    /// <summary>Pasta de logs.</summary>
    public string LogDirectory => Path.Combine(_services.Environment.GetFolder(KnownFolder.LocalAppData), "WinTune", "logs");

    /// <summary>Versão do app exibida/exportada.</summary>
    public string AppVersion { get; set; } = typeof(HistoryViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        foreach (var e in _services.Backup.JournalEntries.Reverse())
        {
            Entries.Add(new HistoryItem(e));
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            _services.FileSystem.CreateDirectory(LogDirectory);
            _services.Shell.OpenFolder(LogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Inform("Logs", "Não foi possível abrir a pasta de logs: " + ex.Message);
        }
    }

    [RelayCommand]
    private Task ExportMarkdownAsync() => ExportAsync(markdown: true);

    [RelayCommand]
    private Task ExportJsonAsync() => ExportAsync(markdown: false);

    private async Task ExportAsync(bool markdown)
    {
        var path = _dialogs.PickSaveFile("Exportar relatório (arquivo local)",
            markdown ? "Markdown (*.md)|*.md" : "JSON (*.json)|*.json",
            $"wintune-relatorio-{_services.Clock.UtcNow.ToLocalTime():yyyyMMdd-HHmm}.{(markdown ? "md" : "json")}");
        if (path is null)
        {
            return;
        }

        var findings = _main.SystemFindings.Concat(_main.GameFindings).Concat(_main.StreamingFindings).Select(f => f.Finding).ToList();
        var profile = _main.Profile;
        var text = await Task.Run(() =>
        {
            var runs = new BenchmarkStore(_services.FileSystem, _services.Environment).LoadAll(out _);
            var builder = new ReportBuilder(_services.Wmi, _services.Environment, _services.Clock);
            var report = builder.Build(AppVersion, profile, findings, _services.Backup, _services.Resolve, runs);
            return markdown ? builder.ToMarkdown(report) : builder.ToJson(report);
        }).ConfigureAwait(true);

        await Task.Run(() => _services.FileSystem.WriteAllTextAtomic(path, text)).ConfigureAwait(true);
        _log($"Relatório exportado para {path}.");
        _dialogs.Inform("Relatório", "Relatório salvo neste PC. Nada foi enviado pela rede.");
    }
}
