using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Analysis;
using FrameForge.App.Services;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Imaging;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// The Develop panel for the viewer's current photo. Sliders render a live preview from a screen-sized copy (only
/// the latest render is shown; older ones are skipped). Changes are saved when the sliders rest for a moment, and
/// each resting point is one undo step, so dragging a slider is one step, not hundreds. The original is never written.
/// </summary>
public sealed partial class DevelopViewModel(IEditRenderService renderer, EditController edits, IFaceLookup faces, MaskListViewModel masks, ILogger<DevelopViewModel> logger) : ObservableObject
{
    /// <summary>Longest side of the preview copy. Large enough for a full-screen view, small enough to render live.</summary>
    public const int PreviewLongSide = 2048;

    private readonly EditHistory _history = new();
    private AnalysisImage? _source;
    private BitmapSource? _originalBitmap;
    private EditRecipe _committed = EditRecipe.Neutral;
    private EditRecipe? _pendingRender;
    private bool _rendering;
    private bool _suppress;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _commitCts;
    private Task _commitChain = Task.CompletedTask;

    /// <summary>How long sliders must rest before a change is saved and becomes an undo step.</summary>
    internal TimeSpan CommitDelay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The latest preview render; exposed so tests can await it.</summary>
    internal Task RenderLoop { get; private set; } = Task.CompletedTask;

    public ObservableCollection<EditPreset> Presets => edits.Presets;

    private bool _masksHooked;

    /// <summary>The Masks section: local adjustments, their masks, the overlay and Refine mode.</summary>
    public MaskListViewModel Masks
    {
        get
        {
            if (!_masksHooked)
            {
                _masksHooked = true;
                masks.RecipeChanged += (_, _) => RecipeChanged();
                masks.MasksChanged += (_, _) => ScheduleRender();
                masks.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MaskListViewModel.IsRefining) && masks.IsRefining && IsCropMode)
                    {
                        IsCropMode = false; // Refine and Crop both use clicks on the photo
                    }
                };
            }

            return masks;
        }
    }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private PhotoTileViewModel? _tile;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsWarning;

    /// <summary>Shows the unedited photo while true (the \ key or the Before button).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private bool _showOriginal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private BitmapSource? _rendered;

    /// <summary>
    /// The edit as rendered, without the mask overlay painted into <see cref="Rendered"/>: what the histogram counts (M09).
    /// </summary>
    public AnalysisImage? RenderedPixels { get; private set; }

    /// <summary>What the photo area shows: the original (Before), the focused Auto option, or the live render.</summary>
    public BitmapSource? DisplayImage => ShowOriginal ? _originalBitmap : FocusedSuggestion?.Preview ?? Rendered;

    // Sliders (bound two-way). Ranges match EditRecipe; values are clamped when the recipe is built.
    [ObservableProperty] private double _exposure;
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private double _tint;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _highlights;
    [ObservableProperty] private double _shadows;
    [ObservableProperty] private double _saturation;
    [ObservableProperty] private double _vibrance;
    [ObservableProperty] private double _straighten;
    [ObservableProperty] private int _quarterTurns;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CropText))]
    private CropRect _crop = CropRect.Full;

    /// <summary>While true the preview shows the whole straightened frame with the crop drawn over it.</summary>
    [ObservableProperty]
    private bool _isCropMode;

    [ObservableProperty]
    private CropAspect _cropAspect = CropAspect.Free;

    /// <summary>Normalised width÷height the crop handles keep, or 0 for free.</summary>
    [ObservableProperty]
    private double _lockedCropRatio;

    public string CropText => Crop.IsFull
        ? "No crop"
        : string.Create(CultureInfo.CurrentCulture, $"Crop {Crop.Width:P0} × {Crop.Height:P0}");

    // Preset saving
    [ObservableProperty] private string _newPresetName = string.Empty;
    [ObservableProperty] private bool _presetIncludesExposure = true;
    [ObservableProperty] private bool _presetIncludesWhiteBalance = true;
    [ObservableProperty] private bool _presetIncludesTone = true;
    [ObservableProperty] private bool _presetIncludesColour = true;
    [ObservableProperty] private bool _presetIncludesGeometry;

    /// <summary>Include the masks and their settings (not the clicks, which belong to this photo).</summary>
    [ObservableProperty] private bool _presetIncludesMasks;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand), nameof(DeletePresetCommand))]
    private EditPreset? _selectedPreset;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>The recipe the sliders describe right now (always valid).</summary>
    public EditRecipe CurrentRecipe => new()
    {
        Exposure = Clamp(Exposure, EditRecipe.MaxExposure),
        Temperature = Clamp(Temperature, EditRecipe.MaxSlider),
        Tint = Clamp(Tint, EditRecipe.MaxSlider),
        Contrast = Clamp(Contrast, EditRecipe.MaxSlider),
        Highlights = Clamp(Highlights, EditRecipe.MaxSlider),
        Shadows = Clamp(Shadows, EditRecipe.MaxSlider),
        Saturation = Clamp(Saturation, EditRecipe.MaxSlider),
        Vibrance = Clamp(Vibrance, EditRecipe.MaxSlider),
        Straighten = Clamp(Straighten, EditRecipe.MaxStraighten),
        QuarterTurns = ((QuarterTurns % 4) + 4) % 4,
        Crop = Crop,
        LocalAdjustments = Masks.ToList(),
    };

    /// <summary>Opens (or switches) the panel to <paramref name="tile"/>, saving any pending change to the previous photo first.</summary>
    public async Task OpenAsync(PhotoTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        await FlushAsync().ConfigureAwait(true);
        IsOpen = true;
        if (ReferenceEquals(Tile, tile) && _source is not null)
        {
            return;
        }

        CloseSuggestions();
        Tile = tile;
        _history.Clear();
        _committed = tile.Recipe;
        var masksOpening = Masks.OpenAsync(tile.Photo); // knows the photo at once; checks the models in the background
        LoadSliders(tile.Recipe);
        NotifyHistory();
        Message = null;
        await LoadSourceAsync(tile).ConfigureAwait(true);
        await masksOpening.ConfigureAwait(true);
    }

    /// <summary>Saves any pending change and closes the panel.</summary>
    public async Task CloseAsync()
    {
        CloseSuggestions();
        await FlushAsync().ConfigureAwait(true);
        _loadCts?.Cancel();
        Masks.Close();
        IsOpen = false;
        IsCropMode = false;
        ShowOriginal = false;
        _source = null;
        _originalBitmap = null;
        RenderedPixels = null;
        Rendered = null;
        Tile = null;
    }

    /// <summary>Saves a pending change now (instead of after the rest delay).</summary>
    public async Task FlushAsync()
    {
        _commitCts?.Cancel();
        _commitCts = null;
        await CommitAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        await FlushAsync().ConfigureAwait(true);
        if (_history.Undo(_committed) is { } previous)
        {
            await RestoreAsync(previous, "Undid the last edit.").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RedoAsync()
    {
        await FlushAsync().ConfigureAwait(true);
        if (_history.Redo(_committed) is { } next)
        {
            await RestoreAsync(next, "Redid the edit.").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ResetAllAsync()
    {
        LoadSliders(EditRecipe.Neutral);
        RecipeChanged();
        await FlushAsync().ConfigureAwait(true);
    }

    /// <summary>Double-click on a slider: back to neutral for that setting.</summary>
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
            case nameof(Straighten): Straighten = 0; break;
        }
    }

    [RelayCommand]
    private void RotateLeft() => QuarterTurns = (QuarterTurns + 3) % 4;

    [RelayCommand]
    private void RotateRight() => QuarterTurns = (QuarterTurns + 1) % 4;

    [RelayCommand]
    private void ToggleCropMode() => IsCropMode = !IsCropMode;

    [RelayCommand]
    private void ResetCrop()
    {
        CropAspect = CropAspect.Free;
        Crop = CropRect.Full;
    }

    [RelayCommand]
    private void ToggleBeforeAfter() => ShowOriginal = !ShowOriginal;

    /// <summary>
    /// A click on the photo in Refine mode, at normalised (u, v) of the shown edit. It is mapped back through crop,
    /// straighten and rotation to the upright photo, where masks and their clicks live. Ignored while the photo shows
    /// something else (Before, an Auto option, crop mode).
    /// </summary>
    [RelayCommand]
    private void AddPoint(Controls.ImagePoint? point)
    {
        if (point is null || !Masks.IsRefining || ShowOriginal || FocusedSuggestion is not null || IsCropMode || _source is not { } source)
        {
            return;
        }

        var (x, y) = new EditGeometry(source.Width, source.Height, CurrentRecipe).ToSource(point.U, point.V);
        Masks.AddPoint(new MaskPoint(Math.Clamp(x / source.Width, 0, 1), Math.Clamp(y / source.Height, 0, 1), point.Include));
    }

    private bool HasSelectedPreset => SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        LoadSliders(preset.ApplyTo(CurrentRecipe));
        RecipeChanged();
        await FlushAsync().ConfigureAwait(true);
        SetMessage($"Applied preset “{preset.Name}”. Ctrl+Z undoes it.", warning: false);
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        var groups = (PresetIncludesExposure ? PresetGroups.Exposure : 0)
            | (PresetIncludesWhiteBalance ? PresetGroups.WhiteBalance : 0)
            | (PresetIncludesTone ? PresetGroups.Tone : 0)
            | (PresetIncludesColour ? PresetGroups.Colour : 0)
            | (PresetIncludesGeometry ? PresetGroups.Geometry : 0)
            | (PresetIncludesMasks ? PresetGroups.Masks : 0);
        var error = await edits.SavePresetAsync(NewPresetName, CurrentRecipe, groups).ConfigureAwait(true);
        if (error is null)
        {
            SetMessage($"Saved preset “{NewPresetName.Trim()}”.", warning: false);
            SelectedPreset = Presets.FirstOrDefault(p => string.Equals(p.Name, NewPresetName.Trim(), StringComparison.OrdinalIgnoreCase));
            NewPresetName = string.Empty;
        }
        else
        {
            SetMessage(error, warning: true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset is { } preset)
        {
            await edits.DeletePresetAsync(preset).ConfigureAwait(true);
            SetMessage($"Deleted preset “{preset.Name}”.", warning: false);
            SelectedPreset = null;
        }
    }

    partial void OnExposureChanged(double value) => RecipeChanged();

    partial void OnTemperatureChanged(double value) => RecipeChanged();

    partial void OnTintChanged(double value) => RecipeChanged();

    partial void OnContrastChanged(double value) => RecipeChanged();

    partial void OnHighlightsChanged(double value) => RecipeChanged();

    partial void OnShadowsChanged(double value) => RecipeChanged();

    partial void OnSaturationChanged(double value) => RecipeChanged();

    partial void OnVibranceChanged(double value) => RecipeChanged();

    partial void OnStraightenChanged(double value) => RecipeChanged();

    partial void OnQuarterTurnsChanged(int value)
    {
        // A different frame shape makes a fixed-ratio crop wrong; fall back to the full frame.
        if (!_suppress && !Crop.IsFull)
        {
            _suppress = true;
            Crop = CropRect.Full;
            CropAspect = CropAspect.Free;
            _suppress = false;
        }

        RecipeChanged();
    }

    partial void OnCropChanged(CropRect value)
    {
        // The crop-mode preview shows the whole frame, so only the saved recipe changes until crop mode ends.
        if (IsCropMode)
        {
            ScheduleCommit();
        }
        else
        {
            RecipeChanged();
        }
    }

    partial void OnIsCropModeChanged(bool value)
    {
        if (value)
        {
            Masks.IsRefining = false;
        }

        if (!_suppress)
        {
            ScheduleRender();
        }
    }

    partial void OnCropAspectChanged(CropAspect value)
    {
        if (_suppress || _source is null)
        {
            return;
        }

        var frame = new EditGeometry(_source.Width, _source.Height, CurrentRecipe with { Crop = CropRect.Full });
        var frameRatio = frame.OutputWidth / frame.OutputHeight;
        if (CropMath.PixelRatio(value, frameRatio) is { } ratio)
        {
            LockedCropRatio = ratio / frameRatio;
            Crop = CropMath.Fit(ratio, frameRatio, Crop);
        }
        else
        {
            LockedCropRatio = 0;
        }
    }

    private void RecipeChanged()
    {
        if (_suppress)
        {
            return;
        }

        // A manual change makes open Auto options stale (they were computed from the previous settings).
        if (IsSuggesting)
        {
            CloseSuggestions();
        }

        ScheduleRender();
        ScheduleCommit();
    }

    private void ScheduleCommit()
    {
        if (_suppress || Tile is null)
        {
            return;
        }

        _commitCts?.Cancel();
        var cts = new CancellationTokenSource();
        _commitCts = cts;
        _ = CommitAfterDelayAsync(cts.Token);
    }

    private async Task CommitAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(CommitDelay, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await CommitAsync().ConfigureAwait(true);
    }

    /// <summary>Commits run one after another, so a flush always waits for a save that is already under way.</summary>
    private Task CommitAsync()
    {
        _commitChain = ChainAsync(_commitChain);
        return _commitChain;

        async Task ChainAsync(Task previous)
        {
            await previous.ConfigureAwait(true);
            await CommitCoreAsync().ConfigureAwait(true);
        }
    }

    private async Task CommitCoreAsync()
    {
        if (Tile is not { } tile)
        {
            return;
        }

        var recipe = CurrentRecipe;
        if (recipe == _committed)
        {
            return;
        }

        var before = _committed;
        _committed = recipe;
        if (await edits.SaveAsync(tile, recipe).ConfigureAwait(true) is { } error)
        {
            // The controller reverted the tile; follow it. A failed save is not an undo step.
            _committed = tile.Recipe;
            LoadSliders(tile.Recipe);
            ScheduleRender();
            SetMessage(error, warning: true);
            return;
        }

        _history.Record(before);
        NotifyHistory();
    }

    private async Task RestoreAsync(EditRecipe recipe, string message)
    {
        if (Tile is not { } tile)
        {
            return;
        }

        _committed = recipe;
        LoadSliders(recipe);
        NotifyHistory();
        ScheduleRender();
        if (await edits.SaveAsync(tile, recipe).ConfigureAwait(true) is { } error)
        {
            SetMessage(error, warning: true);
            return;
        }

        SetMessage(message, warning: false);
    }

    private void LoadSliders(EditRecipe recipe)
    {
        _suppress = true;
        try
        {
            Exposure = recipe.Exposure;
            Temperature = recipe.Temperature;
            Tint = recipe.Tint;
            Contrast = recipe.Contrast;
            Highlights = recipe.Highlights;
            Shadows = recipe.Shadows;
            Saturation = recipe.Saturation;
            Vibrance = recipe.Vibrance;
            Straighten = recipe.Straighten;
            QuarterTurns = recipe.QuarterTurns;
            Crop = recipe.Crop;
            CropAspect = CropAspect.Free;
            LockedCropRatio = 0;
            Masks.Load(recipe.LocalAdjustments);
        }
        finally
        {
            _suppress = false;
        }
    }

    private async Task LoadSourceAsync(PhotoTileViewModel tile)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        _source = null;
        _originalBitmap = null;
        RenderedPixels = null;
        Rendered = null;
        IsLoading = true;
        try
        {
            var source = await renderer.LoadSourceAsync(tile.Photo, PreviewLongSide, cts.Token).ConfigureAwait(true);
            var original = await Task.Run(() => BitmapConversions.ToBitmap(source), cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested && ReferenceEquals(Tile, tile))
            {
                _source = source;
                _originalBitmap = original;
                OnPropertyChanged(nameof(DisplayImage));
                ScheduleRender();
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Switched to another photo.
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, tile.FilePath);
            SetMessage("This photo can't be edited: " + ex.Message, warning: true);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    private void ScheduleRender()
    {
        if (_source is null)
        {
            return;
        }

        _pendingRender = IsCropMode ? CurrentRecipe with { Crop = CropRect.Full } : CurrentRecipe;
        if (!_rendering)
        {
            RenderLoop = RenderLoopAsync();
        }
    }

    /// <summary>Renders the most recent recipe; while one render runs, newer requests replace each other.</summary>
    private async Task RenderLoopAsync()
    {
        _rendering = true;
        try
        {
            while (_pendingRender is { } recipe && _source is { } source)
            {
                _pendingRender = null;

                // Snapshots taken on the UI thread; the render itself runs in the background.
                var ready = Masks.ReadyMasks();
                var overlay = IsCropMode ? null : Masks.OverlayTarget;
                var (pixels, bitmap) = await Task.Run(() =>
                {
                    var rendered = EditRenderer.Render(source, recipe, ready);
                    return (rendered, BitmapConversions.ToBitmap(overlay is { } o ? MaskOverlayPainter.Paint(source, recipe, rendered, o.Mask, o.Invert, o.Points) : rendered));
                }).ConfigureAwait(true);
                if (ReferenceEquals(source, _source))
                {
                    RenderedPixels = pixels; // before Rendered, whose change notification readers of both follow
                    Rendered = bitmap;
                }
            }
        }
        catch (ArgumentException ex)
        {
            LogRenderFailed(ex);
            SetMessage("The preview could not be rendered. See the log for details.", warning: true);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void SetMessage(string text, bool warning)
    {
        Message = text;
        MessageIsWarning = warning;
    }

    private static double Clamp(double value, double limit) => double.IsFinite(value) ? Math.Clamp(value, -limit, limit) : 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Develop could not load {FilePath}")]
    private partial void LogLoadFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Preview render failed.")]
    private partial void LogRenderFailed(Exception exception);
}
