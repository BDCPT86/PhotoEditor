using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.Services;

/// <summary>
/// Keeps tiles' recipes in step with storage, applies presets in batches (undoable), and owns the preset list.
/// UI thread only; storage runs on the thread pool. A failed save reverts the tile so the screen never shows an edit
/// that was not stored.
/// </summary>
public sealed partial class EditController(
    IEditService edits,
    IPresetRepository presets,
    IEditRenderService renderer,
    IFaceLookup faces,
    FrameForge.Application.Editing.Masks.IMaskService masks,
    ILogger<EditController> logger) : ObservableObject
{
    private BatchEditResult? _lastBatch;
    private Dictionary<Guid, PhotoTileViewModel> _tiles = [];

    public ObservableCollection<EditPreset> Presets { get; } = [];

    /// <summary>True when the last preset batch can be undone.</summary>
    [ObservableProperty]
    private bool _canUndoBatch;

    /// <summary>Attaches to a (re)loaded library and applies stored recipes.</summary>
    public async Task LoadAsync(IReadOnlyList<PhotoTileViewModel> tiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = tiles.ToDictionary(t => t.Id);
        var recipes = await edits.LoadAsync(cancellationToken).ConfigureAwait(true);
        foreach (var tile in tiles)
        {
            tile.Recipe = recipes.GetValueOrDefault(tile.Id) ?? EditRecipe.Neutral;
        }

        await LoadPresetsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Shows the recipe immediately and stores it. Returns an error message, or null when saved.</summary>
    public async Task<string?> SaveAsync(PhotoTileViewModel tile, EditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(recipe);
        var previous = tile.Recipe;
        tile.Recipe = recipe;
        try
        {
            await edits.SaveAsync([(tile.Id, recipe)]).ConfigureAwait(true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or ArgumentException or InvalidOperationException)
        {
            LogSaveFailed(ex, tile.FilePath);
            tile.Recipe = previous;
            return "The edit could not be saved, so it was undone. See the log for details.";
        }
    }

    /// <summary>
    /// Why a preset can't be applied right now, or null. A preset with a subject or background mask needs the subject
    /// models; without them every photo would come out without its mask, so the batch is refused up front.
    /// </summary>
    public async Task<string?> WhyPresetCannotRunAsync(EditPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!FrameForge.Application.Editing.Masks.MaskBatch.KindsNeeded(preset).Contains(MaskKind.Subject))
        {
            return null;
        }

        var reason = await Task.Run(() => masks.UnavailableReason(MaskKind.Subject)).ConfigureAwait(true);
        return reason is null ? null : $"“{preset.Name}” has a subject mask, which can't be made yet: {reason}";
    }

    /// <summary>
    /// Applies a preset to the targets as one undoable batch. A preset with masks works them out photo by photo
    /// (slow, cancellable); a mask that can't be made is left off that photo and noted in the result.
    /// </summary>
    public async Task<BatchEditResult> ApplyPresetAsync(IReadOnlyList<PhotoTileViewModel> targets, EditPreset preset, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(preset);
        var current = targets.Select(t => (t.Id, t.Recipe)).ToList();
        BatchEditResult result;
        if (FrameForge.Application.Editing.Masks.MaskBatch.HasMasks(preset))
        {
            var photos = targets.ToDictionary(t => t.Id, t => t.Photo);
            result = await edits.ApplyAsync(
                current,
                (id, recipe, ct) => FrameForge.Application.Editing.Masks.MaskBatch.ApplyAsync(photos[id], recipe, preset, masks, ct),
                progress,
                cancellationToken).ConfigureAwait(true);
        }
        else
        {
            result = await edits.ApplyPresetAsync(current, preset, progress, cancellationToken).ConfigureAwait(true);
        }
        foreach (var (id, _, after) in result.Changes)
        {
            if (_tiles.TryGetValue(id, out var tile))
            {
                tile.Recipe = after;
            }
        }

        _lastBatch = result.Changes.Count > 0 ? result : null;
        CanUndoBatch = _lastBatch is not null;
        return result;
    }

    /// <summary>
    /// Runs the chosen Auto steps on each target and saves the changes as one undoable batch. Photos it changed are
    /// marked <see cref="PhotoTileViewModel.AutoApplied"/> (for the review filter) until the next Auto batch or undo.
    /// </summary>
    public async Task<BatchEditResult> ApplyAutoAsync(IReadOnlyList<PhotoTileViewModel> targets, AutoBatchOptions options, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(options);
        var photos = targets.ToDictionary(t => t.Id, t => t.Photo);
        var result = await edits.ApplyAsync(
            targets.Select(t => (t.Id, t.Recipe)).ToList(),
            (id, current, ct) => AutoBatch.ApplyAsync(photos[id], current, options, renderer, faces, ct),
            progress,
            cancellationToken).ConfigureAwait(true);

        foreach (var tile in _tiles.Values)
        {
            tile.AutoApplied = false;
        }

        foreach (var (id, _, after) in result.Changes)
        {
            if (_tiles.TryGetValue(id, out var tile))
            {
                tile.Recipe = after;
                tile.AutoApplied = true;
            }
        }

        _lastBatch = result.Changes.Count > 0 ? result : null;
        CanUndoBatch = _lastBatch is not null;
        AutoMarksChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>Raised after an Auto batch or an undo changes which tiles are marked Auto-applied.</summary>
    public event EventHandler? AutoMarksChanged;

    /// <summary>Restores every recipe the last batch changed. Returns the number restored.</summary>
    public async Task<int> UndoLastBatchAsync()
    {
        if (_lastBatch is not { } batch)
        {
            return 0;
        }

        await edits.SaveAsync(batch.Changes.Select(c => (c.PhotoId, c.Before)).ToList()).ConfigureAwait(true);
        foreach (var (id, before, _) in batch.Changes)
        {
            if (_tiles.TryGetValue(id, out var tile))
            {
                tile.Recipe = before;
                tile.AutoApplied = false;
            }
        }

        _lastBatch = null;
        CanUndoBatch = false;
        AutoMarksChanged?.Invoke(this, EventArgs.Empty);
        return batch.Changes.Count;
    }

    public async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
        var stored = await presets.GetAllAsync(cancellationToken).ConfigureAwait(true);
        foreach (var (id, reason) in stored.Rejected)
        {
            LogPresetRejected(id, reason);
        }

        Presets.Clear();
        foreach (var preset in stored.Presets)
        {
            Presets.Add(preset);
        }
    }

    /// <summary>Saves a new preset from <paramref name="recipe"/>. Returns an error message, or null.</summary>
    public async Task<string?> SavePresetAsync(string name, EditRecipe recipe, PresetGroups groups)
    {
        var preset = new EditPreset(Guid.NewGuid(), name?.Trim() ?? string.Empty, recipe, groups);
        if (preset.Validate() is { Count: > 0 } problems)
        {
            return problems[0];
        }

        try
        {
            await presets.SaveAsync(preset).ConfigureAwait(true);
        }
        catch (DuplicatePresetNameException ex)
        {
            return ex.Message;
        }

        await LoadPresetsAsync(CancellationToken.None).ConfigureAwait(true);
        return null;
    }

    public async Task DeletePresetAsync(EditPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        await presets.DeleteAsync(preset.Id).ConfigureAwait(true);
        Presets.Remove(preset);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the edit for {FilePath} failed; the tile was reverted.")]
    private partial void LogSaveFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored preset {PresetId} was ignored: {Reason}")]
    private partial void LogPresetRejected(Guid presetId, string reason);
}
