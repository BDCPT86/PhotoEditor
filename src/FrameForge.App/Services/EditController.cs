using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Core.Editing;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.Services;

/// <summary>
/// Keeps tiles' recipes in step with storage, applies presets in batches (undoable), and owns the preset list.
/// UI thread only; storage runs on the thread pool. A failed save reverts the tile so the screen never shows an edit
/// that was not stored.
/// </summary>
public sealed partial class EditController(IEditService edits, IPresetRepository presets, ILogger<EditController> logger) : ObservableObject
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

    public async Task<BatchEditResult> ApplyPresetAsync(IReadOnlyList<PhotoTileViewModel> targets, EditPreset preset, IProgress<BatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var result = await edits.ApplyPresetAsync(targets.Select(t => (t.Id, t.Recipe)).ToList(), preset, progress, cancellationToken).ConfigureAwait(true);
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
            }
        }

        _lastBatch = null;
        CanUndoBatch = false;
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
