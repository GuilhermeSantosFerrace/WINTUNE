using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinTune.Core.Cleanup;

namespace WinTune.Presentation.ViewModels;

/// <summary>Categoria de limpeza na tela.</summary>
public sealed partial class CleanupCategoryViewModel : ObservableObject
{
    public CleanupCategoryViewModel(CleanupCategory category)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        _isSelected = category.DefaultSelected;
    }

    public CleanupCategory Category { get; }

    public string Name => Category.Name;

    public string Description => Category.Description;

    public string? Warning => Category.Warning;

    public bool HasWarning => Category.Warning is not null;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _previewSize = "—";
}

/// <summary>Aba Limpeza: prévia obrigatória antes de apagar; a limpeza não é reversível.</summary>
public sealed partial class CleanupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _log;
    private readonly CleanupService _cleanup;
    private CleanupPlan? _plan;

    public CleanupViewModel(AppServices services, IDialogService dialogs, Action<string> log)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _cleanup = new CleanupService(services.FileSystem, services.Clock, services.Environment, services.Backup);
        foreach (var c in _cleanup.Categories)
        {
            var vm = new CleanupCategoryViewModel(c);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupCategoryViewModel.IsSelected))
                {
                    InvalidatePreview();
                }
            };
            Categories.Add(vm);
        }
    }

    public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = [];

    public ObservableCollection<string> Skipped { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = "Gere a prévia para ver quanto espaço pode ser liberado. Nada é apagado sem a sua confirmação.";

    public static string Irreversible => "Atenção: a limpeza apaga arquivos e NÃO pode ser desfeita (não entra em \"Desfazer tudo\").";

    [RelayCommand]
    private async Task PreviewAsync()
    {
        IsBusy = true;
        try
        {
            var ids = Categories.Where(c => c.IsSelected).Select(c => c.Category.Id).ToList();
            var plan = await Task.Run(() => _cleanup.Preview(ids)).ConfigureAwait(true);
            var byCat = plan.BytesByCategory;
            foreach (var c in Categories)
            {
                c.PreviewSize = c.IsSelected ? Size(byCat.TryGetValue(c.Category.Id, out var b) ? b : 0) : "—";
            }

            Skipped.Clear();
            foreach (var s in plan.Skipped.Take(200))
            {
                Skipped.Add($"{s.Path} — {s.Reason}");
            }

            _plan = plan;
            HasPreview = true;
            Summary = $"Prévia: {plan.Candidates.Count} arquivo(s), {Size(plan.TotalBytes)} podem ser liberados; {plan.Skipped.Count} ignorado(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanClean() => HasPreview;

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (_plan is null)
        {
            return;
        }

        if (!_dialogs.Confirm("Limpeza (irreversível)",
                $"{Summary}\n\n{Irreversible}\n\nApagar agora?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var plan = _plan;
            var report = await Task.Run(() => _cleanup.Execute(plan)).ConfigureAwait(true);
            Summary = $"Limpeza concluída: {Size(report.FreedBytes)} liberados em {report.DeletedFiles} arquivo(s); {report.Skipped.Count} ignorado(s).";
            Skipped.Clear();
            foreach (var s in report.Skipped.Take(200))
            {
                Skipped.Add($"{s.Path} — {s.Reason}");
            }

            _log(Summary);
        }
        finally
        {
            IsBusy = false;
            InvalidatePreview(keepSummary: true);
        }
    }

    private void InvalidatePreview(bool keepSummary = false)
    {
        _plan = null;
        HasPreview = false;
        if (!keepSummary)
        {
            Summary = "A seleção mudou: gere a prévia novamente.";
        }
    }

    internal static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " MB",
        _ => (bytes / 1024.0).ToString("0", CultureInfo.GetCultureInfo("pt-BR")) + " KB",
    };
}
