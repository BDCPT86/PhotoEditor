using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Analysis;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;
using FrameForge.Imaging;
using FrameForge.Rendering;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Auto tools in Develop. Each tool computes a few options from the photo (never a model, never applied by itself),
/// renders each one, and opens the suggestions tray. Focusing an option shows it on the photo; accepting it is one
/// undo step; dismissing changes nothing. Moving a slider or switching photos closes stale suggestions.
/// </summary>
public sealed partial class DevelopViewModel
{
    private CancellationTokenSource? _autoCts;

    public ObservableCollection<SuggestionItem> Suggestions { get; } = [];

    // Which panel sections are open. Kept for the session (the view model lives as long as the app).
    [ObservableProperty] private bool _isAutoSectionOpen = true;
    [ObservableProperty] private bool _isLightSectionOpen = true;
    [ObservableProperty] private bool _isColourSectionOpen = true;
    [ObservableProperty] private bool _isCropSectionOpen;
    [ObservableProperty] private bool _isPresetsSectionOpen;

    /// <summary>The tray is open (with options, or with a message saying why there are none).</summary>
    [ObservableProperty]
    private bool _isSuggesting;

    [ObservableProperty]
    private bool _isComputingSuggestions;

    [ObservableProperty]
    private string? _suggestionTitle;

    /// <summary>Why the tool has no options (shown in the tray instead of thumbnails).</summary>
    [ObservableProperty]
    private string? _suggestionMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage), nameof(FocusedGuide))]
    private SuggestionItem? _focusedSuggestion;

    /// <summary>A guide line to draw over the focused option (Auto level), or null.</summary>
    public GuideLine? FocusedGuide => FocusedSuggestion?.Guide;

    /// <summary>Short label for the Auto crop button, naming the ratio it will use.</summary>
    public string AutoCropLabel => CropAspect == CropAspect.Free ? "Crop · original ratio" : $"Crop · {AspectText(CropAspect)}";

    partial void OnFocusedSuggestionChanged(SuggestionItem? oldValue, SuggestionItem? newValue)
    {
        oldValue?.IsFocused = false;
        newValue?.IsFocused = true;
    }

    [RelayCommand]
    private Task AutoLightAsync() => SuggestAsync(AutoTone.Title, (source, recipe, _) => AutoTone.Suggest(source, recipe));

    [RelayCommand]
    private Task AutoLevelAsync() => SuggestAsync(AutoStraighten.Title, (source, recipe, ct) => AutoStraighten.Suggest(source, recipe, ct));

    [RelayCommand]
    private Task AutoCropAsync()
    {
        if (Tile is not { } tile)
        {
            return Task.CompletedTask;
        }

        if (!faces.HasFaceData(tile.Id))
        {
            ShowUnavailable(AutoCrop.Title, "Auto crop frames faces, and this photo has not been analysed for faces yet. Turn on faces in Settings → AI models, then run Analyze.");
            return Task.CompletedTask;
        }

        var found = faces.FacesOf(tile.Id);
        var minScore = faces.FaceMinScore;
        return SuggestAsync(AutoCrop.Title, (source, recipe, _) =>
        {
            var (w, h) = EditRenderService.OrientedSize(tile.Photo) ?? (source.Width, source.Height);
            var frame = new EditGeometry(source.Width, source.Height, recipe with { Crop = CropRect.Full });
            var frameRatio = frame.OutputWidth / frame.OutputHeight;
            return AutoCrop.Suggest(w, h, found, recipe, CropMath.PixelRatio(CropAspect, frameRatio), minScore);
        });
    }

    /// <summary>Accepts the focused option (or the one given): one undo step, saved like any edit.</summary>
    [RelayCommand]
    private async Task AcceptSuggestionAsync(SuggestionItem? item)
    {
        item ??= FocusedSuggestion;
        if (item is null)
        {
            return;
        }

        var title = SuggestionTitle;
        CloseSuggestions();
        LoadSliders(item.Suggestion.Recipe);
        RecipeChanged();
        await FlushAsync().ConfigureAwait(true);
        SetMessage($"{title}: applied “{item.Name}”. Ctrl+Z undoes it.", warning: false);
    }

    [RelayCommand]
    private void DismissSuggestions() => CloseSuggestions();

    [RelayCommand]
    private void FocusSuggestion(SuggestionItem? item)
    {
        if (item is not null)
        {
            FocusedSuggestion = item;
        }
    }

    /// <summary>Keyboard in the tray: ← → move, 1–3 pick, Enter accepts, Esc dismisses. Returns true when handled.</summary>
    public bool HandleSuggestionKey(System.Windows.Input.Key key)
    {
        if (!IsSuggesting)
        {
            return false;
        }

        var index = FocusedSuggestion is { } f ? Suggestions.IndexOf(f) : -1;
        switch (key)
        {
            case System.Windows.Input.Key.Escape:
                CloseSuggestions();
                return true;
            case System.Windows.Input.Key.Enter when FocusedSuggestion is not null:
                AcceptSuggestionCommand.Execute(null);
                return true;
            case System.Windows.Input.Key.Left when Suggestions.Count > 0:
                FocusedSuggestion = Suggestions[Math.Max(0, index - 1)];
                return true;
            case System.Windows.Input.Key.Right when Suggestions.Count > 0:
                FocusedSuggestion = Suggestions[Math.Min(Suggestions.Count - 1, index + 1)];
                return true;
            case >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9:
            case >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9:
                var n = key >= System.Windows.Input.Key.NumPad1 ? key - System.Windows.Input.Key.NumPad1 : key - System.Windows.Input.Key.D1;
                if (n < Suggestions.Count)
                {
                    FocusedSuggestion = Suggestions[n];
                }

                return true; // digits never fall through to ratings while the tray is open
            default:
                return false;
        }
    }

    /// <summary>Closes the tray without changing anything.</summary>
    public void CloseSuggestions()
    {
        _autoCts?.Cancel();
        Suggestions.Clear();
        FocusedSuggestion = null;
        IsSuggesting = false;
        IsComputingSuggestions = false;
        SuggestionMessage = null;
        SuggestionTitle = null;
    }

    partial void OnCropAspectChanged(CropAspect oldValue, CropAspect newValue) => OnPropertyChanged(nameof(AutoCropLabel));

    private async Task SuggestAsync(string title, Func<AnalysisImage, EditRecipe, CancellationToken, AutoSuggestions> compute)
    {
        if (_source is not { } source || Tile is null)
        {
            return;
        }

        CloseSuggestions();
        await FlushAsync().ConfigureAwait(true);
        var cts = new CancellationTokenSource();
        _autoCts = cts;
        var recipe = CurrentRecipe;
        SuggestionTitle = title;
        IsSuggesting = true;
        IsComputingSuggestions = true;
        try
        {
            var (result, previews) = await Task.Run(
                () =>
                {
                    var suggestions = compute(source, recipe, cts.Token);
                    var rendered = suggestions.Options
                        .AsParallel().AsOrdered()
                        .Select(o => BitmapConversions.ToBitmap(EditRenderer.Render(source, o.Recipe, null, cts.Token)))
                        .ToList();
                    return (suggestions, rendered);
                },
                cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (!result.HasOptions)
            {
                SuggestionMessage = result.Unavailable;
                return;
            }

            for (var i = 0; i < result.Options.Count; i++)
            {
                Suggestions.Add(new SuggestionItem(result.Options[i], i + 1, previews[i]));
            }

            FocusedSuggestion = Suggestions[0];
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded or dismissed.
        }
        finally
        {
            if (ReferenceEquals(_autoCts, cts))
            {
                IsComputingSuggestions = false;
            }
        }
    }

    private void ShowUnavailable(string title, string message)
    {
        CloseSuggestions();
        SuggestionTitle = title;
        SuggestionMessage = message;
        IsSuggesting = true;
    }

    internal static string AspectText(CropAspect aspect) => aspect switch
    {
        CropAspect.Original => "original",
        CropAspect.Square => "1:1",
        CropAspect.Portrait4x5 => "4:5",
        CropAspect.Classic3x2 => "3:2",
        CropAspect.Wide16x9 => "16:9",
        _ => "free",
    };

    internal static string Percent(double value) => value.ToString("P0", CultureInfo.CurrentCulture);
}
