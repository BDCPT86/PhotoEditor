using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Develop's Masks section: the photo's local adjustments, their masks (computed on demand, one at a time, with
/// cancel), the mask overlay and Refine mode (clicks that add to or remove from the selected mask). The masks belong
/// to the recipe, so every change goes through Develop's normal save and undo.
/// </summary>
public sealed partial class MaskListViewModel(IMaskService masks, ILogger<MaskListViewModel> logger) : ObservableObject
{
    private readonly Dictionary<Guid, (MaskReference For, MaskImage Mask)> _ready = [];
    private readonly Dictionary<Guid, (CancellationTokenSource Cts, MaskReference For)> _computing = [];
    private readonly List<Task> _running = [];
    private Photo? _photo;
    private bool _suppress;

    /// <summary>A setting, the list or a mask reference changed: Develop renders and saves (one undo step per rest).</summary>
    public event EventHandler? RecipeChanged;

    /// <summary>A mask became available or the overlay changed: Develop renders again.</summary>
    public event EventHandler? MasksChanged;

    public ObservableCollection<LocalAdjustmentViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(OverlayTarget))]
    [NotifyCanExecuteChangedFor(nameof(ClearClicksCommand), nameof(ToggleRefineCommand))]
    private LocalAdjustmentViewModel? _selected;

    /// <summary>Shows the selected mask over the photo (O): outside it is darkened, its edge outlined.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayTarget))]
    private bool _showOverlay;

    /// <summary>Clicks on the photo add to (click) or remove from (Alt+click or right-click) the selected mask (M).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayTarget))]
    private bool _isRefining;

    /// <summary>Why Subject and Background cannot be added (models not installed or failed to load), or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSubjectCommand), nameof(AddBackgroundCommand))]
    private string? _subjectUnavailable;

    /// <summary>Completes when every mask being computed now has finished (for tests).</summary>
    internal Task WhenIdle => Task.WhenAll(_running.ToArray());

    public bool HasSelection => Selected is not null;

    public bool HasItems => Items.Count > 0;

    public string CountText => $"{Items.Count} of {EditRecipe.MaxLocalAdjustments}";

    /// <summary>The mask to draw over the photo, or null when the overlay is off or the mask is not ready.</summary>
    public (MaskImage Mask, bool Invert, IReadOnlyList<MaskPoint> Points)? OverlayTarget =>
        (ShowOverlay || IsRefining) && Selected is { } item && ReadyMask(item) is { } mask
            ? (mask, item.Invert, item.Points)
            : null;

    /// <summary>Starts working on a photo: checks (off the UI thread) whether Subject masks can be made.</summary>
    public async Task OpenAsync(Photo photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (!ReferenceEquals(_photo, photo))
        {
            CancelAll();
            _ready.Clear();
        }

        _photo = photo;
        SubjectUnavailable = await Task.Run(() => masks.UnavailableReason(MaskKind.Subject)).ConfigureAwait(true);
    }

    /// <summary>Stops all work and forgets the photo.</summary>
    public void Close()
    {
        CancelAll();
        _ready.Clear();
        _photo = null;
        IsRefining = false;
        _suppress = true;
        Items.Clear();
        Selected = null;
        _suppress = false;
        NotifyList();
    }

    /// <summary>Shows a recipe's local adjustments (opening a photo, undo, a preset), keeping masks that still fit.</summary>
    public void Load(IReadOnlyList<LocalAdjustment> adjustments)
    {
        ArgumentNullException.ThrowIfNull(adjustments);
        _suppress = true;
        try
        {
            var selectedId = Selected?.Id;
            var existing = Items.ToDictionary(i => i.Id);
            Items.Clear();
            foreach (var model in adjustments)
            {
                if (existing.TryGetValue(model.Id, out var item) && item.Kind == model.Mask.Kind)
                {
                    item.Load(model);
                }
                else
                {
                    item = new LocalAdjustmentViewModel(model, OnItemChanged);
                }

                Items.Add(item);
            }

            foreach (var gone in existing.Keys.Where(id => Items.All(i => i.Id != id)))
            {
                Cancel(gone);
                _ready.Remove(gone);
            }

            Selected = Items.FirstOrDefault(i => i.Id == selectedId) ?? Items.FirstOrDefault();
            if (Selected is null)
            {
                IsRefining = false;
            }
        }
        finally
        {
            _suppress = false;
        }

        NotifyList();
        foreach (var item in Items)
        {
            EnsureMask(item);
        }
    }

    /// <summary>The adjustments as stored in the recipe.</summary>
    public ValueList<LocalAdjustment> ToList() => Items.Count == 0 ? ValueList.Empty<LocalAdjustment>() : new(Items.Select(i => i.ToModel()));

    /// <summary>Masks that are ready for the current references, by adjustment id, for the renderer.</summary>
    public IReadOnlyDictionary<Guid, MaskImage> ReadyMasks() =>
        Items.Select(i => (i.Id, Mask: ReadyMask(i))).Where(p => p.Mask is not null).ToDictionary(p => p.Id, p => p.Mask!);

    private bool CanAddSubject => SubjectUnavailable is null && CanAdd;

    private bool CanAdd => Items.Count < EditRecipe.MaxLocalAdjustments && _photo is not null;

    [RelayCommand(CanExecute = nameof(CanAddSubject))]
    private void AddSubject() => Add(MaskKind.Subject, invert: false);

    [RelayCommand(CanExecute = nameof(CanAddSubject))]
    private void AddBackground() => Add(MaskKind.Subject, invert: true);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddSky() => Add(MaskKind.Sky, invert: false);

    [RelayCommand]
    private void Delete(LocalAdjustmentViewModel? item)
    {
        if (item is null || !Items.Contains(item))
        {
            return;
        }

        var index = Items.IndexOf(item);
        Cancel(item.Id);
        _ready.Remove(item.Id);
        Items.Remove(item);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        if (Selected is null)
        {
            IsRefining = false;
        }

        NotifyList();
        RecipeChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Retry(LocalAdjustmentViewModel? item)
    {
        if (item is not null)
        {
            _ready.Remove(item.Id);
            EnsureMask(item);
        }
    }

    [RelayCommand]
    private void CancelComputing(LocalAdjustmentViewModel? item)
    {
        if (item is not null)
        {
            Cancel(item.Id);
        }
    }

    private bool CanClearClicks => Selected is { HasClicks: true };

    [RelayCommand(CanExecute = nameof(CanClearClicks))]
    private void ClearClicks() => Selected?.SetPoints(ValueList.Empty<MaskPoint>());

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleRefine() => IsRefining = !IsRefining && Selected is not null;

    [RelayCommand]
    private void ToggleOverlay() => ShowOverlay = !ShowOverlay;

    /// <summary>A click in Refine mode, already mapped to the upright photo before crop (0–1).</summary>
    public void AddPoint(MaskPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (!IsRefining || Selected is not { } item || item.Points.Count >= MaskReference.MaxPoints)
        {
            return;
        }

        item.SetPoints(item.Points.Add(point));
    }

    partial void OnSelectedChanged(LocalAdjustmentViewModel? value)
    {
        if (!_suppress)
        {
            MasksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnShowOverlayChanged(bool value) => MasksChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsRefiningChanged(bool value) => MasksChanged?.Invoke(this, EventArgs.Empty);

    private void Add(MaskKind kind, bool invert)
    {
        var model = new LocalAdjustment
        {
            Id = Guid.NewGuid(),
            Mask = new MaskReference { Kind = kind, Source = masks.CurrentSource(kind) ?? kind.ToString(), Invert = invert },
        };
        var item = new LocalAdjustmentViewModel(model, OnItemChanged);
        Items.Add(item);
        Selected = item;
        ShowOverlay = true; // show what was selected; O hides it again
        NotifyList();
        RecipeChanged?.Invoke(this, EventArgs.Empty);
        EnsureMask(item);
    }

    private void OnItemChanged(LocalAdjustmentViewModel item)
    {
        ClearClicksCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(OverlayTarget));
        RecipeChanged?.Invoke(this, EventArgs.Empty);
        EnsureMask(item); // a click or a newer source needs a new mask; sliders and invert do not
    }

    /// <summary>The ready mask for the item's current clicks and source (inversion does not matter), or null.</summary>
    private MaskImage? ReadyMask(LocalAdjustmentViewModel item) =>
        _ready.TryGetValue(item.Id, out var ready) && SameMask(ready.For, item.Reference) ? ready.Mask : null;

    private static bool SameMask(MaskReference a, MaskReference b) => a with { Invert = false } == b with { Invert = false };

    /// <summary>Starts computing the item's mask unless it is ready or already being computed for these clicks.</summary>
    private void EnsureMask(LocalAdjustmentViewModel item)
    {
        if (ReadyMask(item) is not null)
        {
            item.Unavailable = null;
            return;
        }

        if (_photo is not { } photo
            || (_computing.TryGetValue(item.Id, out var running) && SameMask(running.For, item.Reference)))
        {
            return; // no photo, or already being computed for these clicks (a slider moved meanwhile)
        }

        var task = ComputeAsync(photo, item);
        _running.Add(task);
        _running.RemoveAll(t => t.IsCompleted);
    }

    private async Task ComputeAsync(Photo photo, LocalAdjustmentViewModel item)
    {
        Cancel(item.Id);
        var cts = new CancellationTokenSource();
        var reference = item.Reference;
        _computing[item.Id] = (cts, reference);
        item.IsComputing = true;
        item.Unavailable = null;
        try
        {
            var outcome = await masks.GetAsync(photo, reference, cts.Token).ConfigureAwait(true);
            if (!ReferenceEquals(_photo, photo) || !Items.Contains(item))
            {
                return;
            }

            if (outcome.Mask is { } mask)
            {
                var made = reference with { Source = outcome.Source };
                _ready[item.Id] = (made, mask);
                item.Summary = outcome.Summary;
                item.Unavailable = null;
                if (item.Source != outcome.Source && SameMask(reference, item.Reference))
                {
                    // Made by a newer model or rule set: the reference follows, so the cache key matches next time.
                    item.Source = outcome.Source;
                    RecipeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                item.Summary = null;
                item.Unavailable = outcome.Summary;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Cancel() has already said so, or a newer computation for new clicks replaced this one.
            return;
        }
        finally
        {
            if (_computing.TryGetValue(item.Id, out var current) && ReferenceEquals(current.Cts, cts))
            {
                _computing.Remove(item.Id);
                item.IsComputing = false;
            }

            cts.Dispose();
        }

        LogMask(item.Kind, item.StatusText);
        OnPropertyChanged(nameof(OverlayTarget));
        MasksChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel(Guid id)
    {
        if (_computing.Remove(id, out var running))
        {
            running.Cts.Cancel();
            if (Items.FirstOrDefault(i => i.Id == id) is { } item)
            {
                item.IsComputing = false;
                if (ReadyMask(item) is null)
                {
                    item.Unavailable = "Cancelled. Retry to compute this mask.";
                }
            }
        }
    }

    private void CancelAll()
    {
        foreach (var id in _computing.Keys.ToList())
        {
            Cancel(id);
        }
    }

    private void NotifyList()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(OverlayTarget));
        AddSubjectCommand.NotifyCanExecuteChanged();
        AddBackgroundCommand.NotifyCanExecuteChanged();
        AddSkyCommand.NotifyCanExecuteChanged();
        ToggleRefineCommand.NotifyCanExecuteChanged();
        ClearClicksCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Kind} mask: {Status}")]
    private partial void LogMask(MaskKind kind, string status);
}
