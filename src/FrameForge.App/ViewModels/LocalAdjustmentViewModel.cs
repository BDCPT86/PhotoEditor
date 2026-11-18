using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Core.Editing;

namespace FrameForge.App.ViewModels;

/// <summary>
/// One mask in Develop's Masks section: what it selects, its own sliders (the same ranges as the global ones), and
/// whether its mask is ready, being computed, or unavailable and why.
/// </summary>
public sealed partial class LocalAdjustmentViewModel : ObservableObject
{
    private readonly Action<LocalAdjustmentViewModel> _changed;
    private bool _suppress;

    internal LocalAdjustmentViewModel(LocalAdjustment model, Action<LocalAdjustmentViewModel> changed)
    {
        Id = model.Id;
        Kind = model.Mask.Kind;
        _changed = changed;
        Load(model);
    }

    public Guid Id { get; }

    public MaskKind Kind { get; }

    /// <summary>What makes the mask (updated when a newer model or rule set made it).</summary>
    public string Source { get; internal set; } = string.Empty;

    /// <summary>Clicks that add to or remove from the mask, normalised to the upright photo before crop.</summary>
    public ValueList<MaskPoint> Points { get; private set; } = ValueList.Empty<MaskPoint>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    private bool _invert;

    [ObservableProperty] private double _exposure;
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private double _tint;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _highlights;
    [ObservableProperty] private double _shadows;
    [ObservableProperty] private double _saturation;
    [ObservableProperty] private double _vibrance;

    /// <summary>Subject, Background, Sky or Not sky.</summary>
    public string Name => Reference.DisplayName;

    /// <summary>True while the mask is being computed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsUnavailable))]
    private bool _isComputing;

    /// <summary>What was found ("2 people"), when the mask is ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _summary;

    /// <summary>Why there is no mask (shown with a warning sign), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsUnavailable))]
    private string? _unavailable;

    public bool IsUnavailable => !IsComputing && Unavailable is not null;

    public string StatusText => IsComputing
        ? Kind == MaskKind.Subject ? "Finding the subject…" : "Finding the sky…"
        : Unavailable ?? Summary ?? string.Empty;

    public bool HasClicks => Points.Count > 0;

    public string ClicksText => Points.Count switch
    {
        0 => "No clicks",
        1 => "1 click",
        _ => $"{Points.Count} clicks",
    };

    /// <summary>The mask this adjustment needs (inversion included).</summary>
    public MaskReference Reference => new() { Kind = Kind, Source = Source, Invert = Invert, Points = Points };

    public LocalAdjustment ToModel() => new()
    {
        Id = Id,
        Mask = Reference,
        Exposure = Clamp(Exposure, EditRecipe.MaxExposure),
        Temperature = Clamp(Temperature, EditRecipe.MaxSlider),
        Tint = Clamp(Tint, EditRecipe.MaxSlider),
        Contrast = Clamp(Contrast, EditRecipe.MaxSlider),
        Highlights = Clamp(Highlights, EditRecipe.MaxSlider),
        Shadows = Clamp(Shadows, EditRecipe.MaxSlider),
        Saturation = Clamp(Saturation, EditRecipe.MaxSlider),
        Vibrance = Clamp(Vibrance, EditRecipe.MaxSlider),
    };

    /// <summary>Takes the stored values (after undo, a preset or opening the photo) without reporting a change.</summary>
    internal void Load(LocalAdjustment model)
    {
        _suppress = true;
        try
        {
            Source = model.Mask.Source;
            Points = model.Mask.Points;
            Invert = model.Mask.Invert;
            Exposure = model.Exposure;
            Temperature = model.Temperature;
            Tint = model.Tint;
            Contrast = model.Contrast;
            Highlights = model.Highlights;
            Shadows = model.Shadows;
            Saturation = model.Saturation;
            Vibrance = model.Vibrance;
            OnPropertyChanged(nameof(HasClicks));
            OnPropertyChanged(nameof(ClicksText));
        }
        finally
        {
            _suppress = false;
        }
    }

    internal void SetPoints(ValueList<MaskPoint> points)
    {
        Points = points;
        OnPropertyChanged(nameof(HasClicks));
        OnPropertyChanged(nameof(ClicksText));
        Changed();
    }

    /// <summary>Double-click on one of this mask's sliders: back to 0 for that setting.</summary>
    [RelayCommand]
    private void ResetSetting(string? name)
    {
        switch (name)
        {
            case nameof(Exposure): Exposure = 0; break;
            case nameof(Temperature): Temperature = 0; break;
            case nameof(Tint): Tint = 0; break;
            case nameof(Contrast): Contrast = 0; break;
            case nameof(Highlights): Highlights = 0; break;
            case nameof(Shadows): Shadows = 0; break;
            case nameof(Saturation): Saturation = 0; break;
            case nameof(Vibrance): Vibrance = 0; break;
        }
    }

    partial void OnInvertChanged(bool value) => Changed();

    partial void OnExposureChanged(double value) => Changed();

    partial void OnTemperatureChanged(double value) => Changed();

    partial void OnTintChanged(double value) => Changed();

    partial void OnContrastChanged(double value) => Changed();

    partial void OnHighlightsChanged(double value) => Changed();

    partial void OnShadowsChanged(double value) => Changed();

    partial void OnSaturationChanged(double value) => Changed();

    partial void OnVibranceChanged(double value) => Changed();

    private void Changed()
    {
        if (!_suppress)
        {
            _changed(this);
        }
    }

    private static double Clamp(double value, double limit) => double.IsFinite(value) ? Math.Clamp(value, -limit, limit) : 0;
}
