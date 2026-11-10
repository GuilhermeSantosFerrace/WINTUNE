using CommunityToolkit.Mvvm.ComponentModel;
using WinTune.Core.Domain;
using WinTune.Core.Recommendations;
using WinTune.Core.Tweaks;

namespace WinTune.Presentation.ViewModels;

/// <summary>Estado geral da tela.</summary>
public enum AppState
{
    /// <summary>Ocioso.</summary>
    Idle,

    /// <summary>Analisando.</summary>
    Analyzing,

    /// <summary>Aplicando ou revertendo.</summary>
    Applying,
}

/// <summary>Checkbox de cenário.</summary>
public sealed partial class ScenarioOption : ObservableObject
{
    public ScenarioOption(Scenario scenario, string label)
    {
        Scenario = scenario;
        Label = label;
    }

    public Scenario Scenario { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>Linha da lista de ajustes recomendados.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    public TweakItemViewModel(Recommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        Tweak = recommendation.Tweak;
        _isSelected = recommendation.Selected;
        AlreadyApplied = recommendation.AlreadyApplied;
        Note = recommendation.Note;
        _status = recommendation.AlreadyApplied ? "Já aplicado" : "Não aplicado";
    }

    public Tweak Tweak { get; }

    public string Id => Tweak.Id;

    public string Name => Tweak.Name;

    public string Description => Tweak.Description;

    public string Mechanism => Tweak.Mechanism;

    public bool AlreadyApplied { get; }

    public string? Note { get; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool RequiresReboot => Tweak.RequiresReboot;

    public string RiskText => Tweak.Risk switch
    {
        Risk.Safe => "Seguro",
        Risk.Cosmetic => "Cosmético",
        Risk.NeedsReboot => "Exige reinício",
        _ => Tweak.Risk.ToString(),
    };

    public string AccessibleName => $"{Name}. Risco: {RiskText}. Estado: {Status}.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string _status;
}

/// <summary>Linha de achado do diagnóstico.</summary>
public sealed class FindingItemViewModel
{
    public FindingItemViewModel(Finding finding)
    {
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
    }

    public Finding Finding { get; }

    public string Title => Finding.Title;

    public string Message => Finding.Message;

    public string? Evidence => Finding.Evidence;

    public bool HasEvidence => !string.IsNullOrEmpty(Finding.Evidence);

    public Severity Severity => Finding.Severity;

    public string SeverityText => Finding.Severity switch
    {
        Severity.Critical => "Impacto alto",
        Severity.Warning => "Atenção",
        Severity.Error => "Não avaliado",
        _ => "Informação",
    };

    public string AccessibleName => $"{SeverityText}: {Title}";
}
