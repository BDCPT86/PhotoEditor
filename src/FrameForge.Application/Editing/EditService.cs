using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Editing;

public sealed record BatchProgress(int Total, int Completed);

/// <summary>What a batch changed, so it can be undone exactly.</summary>
public sealed record BatchEditResult(IReadOnlyList<(Guid PhotoId, EditRecipe Before, EditRecipe After)> Changes, bool WasCancelled)
{
    /// <summary>Per-photo notes, e.g. a step that could not be applied (Auto crop with no faces).</summary>
    public IReadOnlyList<(Guid PhotoId, string Note)> Notes { get; init; } = [];
}

public interface IEditService
{
    /// <summary>All stored recipes (photos without one are neutral). Unreadable rows are logged and treated as neutral.</summary>
    Task<IReadOnlyDictionary<Guid, EditRecipe>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="preset"/> to each target's current recipe and saves in batches. On cancellation the
    /// batches already saved stay saved and are reported, so they can be undone.
    /// </summary>
    Task<BatchEditResult> ApplyPresetAsync(
        IReadOnlyList<(Guid PhotoId, EditRecipe Current)> targets,
        EditPreset preset,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="transform"/> on each photo (one at a time) and saves changed recipes in small batches.
    /// Photos it leaves unchanged are not saved. Cancellation keeps what was saved and reports it, so it can be undone.
    /// </summary>
    Task<BatchEditResult> ApplyAsync(
        IReadOnlyList<(Guid PhotoId, EditRecipe Current)> targets,
        Func<Guid, EditRecipe, CancellationToken, Task<(EditRecipe? After, string? Note)>> transform,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed partial class EditService(IEditRepository repository, ILogger<EditService> logger) : IEditService
{
    internal const int BatchSize = 100;

    public async Task<IReadOnlyDictionary<Guid, EditRecipe>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (id, reason) in stored.Rejected)
        {
            LogRejected(id, reason);
        }

        return stored.Recipes;
    }

    public Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default) =>
        repository.SaveAsync(recipes, cancellationToken);

    public async Task<BatchEditResult> ApplyPresetAsync(
        IReadOnlyList<(Guid PhotoId, EditRecipe Current)> targets,
        EditPreset preset,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Validate() is { Count: > 0 } problems)
        {
            throw new ArgumentException("Invalid preset: " + string.Join(" ", problems), nameof(preset));
        }

        var changes = new List<(Guid, EditRecipe, EditRecipe)>();
        var done = 0;
        progress?.Report(new BatchProgress(targets.Count, 0));
        try
        {
            foreach (var chunk in targets.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = chunk.Select(t => (t.PhotoId, Before: t.Current, After: preset.ApplyTo(t.Current))).ToList();
                await repository.SaveAsync(batch.Select(b => (b.PhotoId, b.After)).ToList(), CancellationToken.None).ConfigureAwait(false);
                changes.AddRange(batch);
                done += chunk.Length;
                progress?.Report(new BatchProgress(targets.Count, done));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogBatchCancelled(preset.Name, done, targets.Count);
            return new BatchEditResult(changes, WasCancelled: true);
        }

        LogBatchApplied(preset.Name, done);
        return new BatchEditResult(changes, WasCancelled: false);
    }

    public async Task<BatchEditResult> ApplyAsync(
        IReadOnlyList<(Guid PhotoId, EditRecipe Current)> targets,
        Func<Guid, EditRecipe, CancellationToken, Task<(EditRecipe? After, string? Note)>> transform,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(transform);
        const int saveEvery = 25;
        var changes = new List<(Guid, EditRecipe, EditRecipe)>();
        var notes = new List<(Guid, string)>();
        var pending = new List<(Guid PhotoId, EditRecipe Before, EditRecipe After)>();
        var done = 0;
        var cancelled = false;

        async Task SaveAsync()
        {
            if (pending.Count > 0)
            {
                await repository.SaveAsync(pending.Select(p => (p.PhotoId, p.After)).ToList(), CancellationToken.None).ConfigureAwait(false);
                changes.AddRange(pending);
                pending.Clear();
            }
        }

        progress?.Report(new BatchProgress(targets.Count, 0));
        try
        {
            foreach (var (id, current) in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (after, note) = await transform(id, current, cancellationToken).ConfigureAwait(false);
                if (after is not null && after != current)
                {
                    pending.Add((id, current, after.EnsureValid()));
                }

                if (note is not null)
                {
                    notes.Add((id, note));
                }

                done++;
                if (pending.Count >= saveEvery)
                {
                    await SaveAsync().ConfigureAwait(false);
                }

                progress?.Report(new BatchProgress(targets.Count, done));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            await SaveAsync().ConfigureAwait(false);
        }

        LogBatchRun(done, targets.Count, changes.Count, notes.Count, cancelled);
        return new BatchEditResult(changes, cancelled) { Notes = notes };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch edit: {Done} of {Total} photo(s) processed, {Changed} changed, {Notes} with notes, cancelled={Cancelled}")]
    private partial void LogBatchRun(int done, int total, int changed, int notes, bool cancelled);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored edit for photo {PhotoId} was ignored: {Reason}")]
    private partial void LogRejected(Guid photoId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Preset {Preset} applied to {Count} photo(s)")]
    private partial void LogBatchApplied(string preset, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Preset {Preset} cancelled after {Done} of {Total} photo(s); those stay applied")]
    private partial void LogBatchCancelled(string preset, int done, int total);
}

/// <summary>
/// The masks a recipe's local adjustments need, by adjustment id, and the reason for each one that is unavailable
/// (an adjustment without a mask is left out of the render).
/// </summary>
public sealed record LocalMasks(IReadOnlyDictionary<Guid, MaskImage> Masks, IReadOnlyList<(Guid Id, string Reason)> Unavailable)
{
    public static LocalMasks None { get; } = new(new Dictionary<Guid, MaskImage>(), []);
}

/// <summary>Decodes a photo at the size a render needs and renders a recipe onto it.</summary>
public interface IEditRenderService
{
    /// <summary>Decodes the original (read-only) at most <paramref name="maxLongSide"/> on its longest side, upright.</summary>
    Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes at the size the output needs, then renders, including local adjustments whose masks can be made
    /// (computed on demand and cached). Null = full resolution.
    /// </summary>
    Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken);

    /// <summary>Gets (or computes) the masks for a recipe's local adjustments. Never throws for model or file problems.</summary>
    Task<LocalMasks> LoadMasksAsync(Photo photo, EditRecipe recipe, CancellationToken cancellationToken);
}

public sealed partial class EditRenderService(IAnalysisImageLoader loader, Masks.IMaskService masks, ILogger<EditRenderService> logger) : IEditRenderService
{
    public Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return Task.Run(() => loader.Load(photo.FilePath, maxLongSide ?? int.MaxValue), cancellationToken);
    }

    public async Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(recipe);
        var local = await LoadMasksAsync(photo, recipe, cancellationToken).ConfigureAwait(false);
        foreach (var (id, reason) in local.Unavailable)
        {
            LogMaskLeftOut(photo.Id, id, reason);
        }

        return await Task.Run(
            () =>
            {
                var decodeSide = OrientedSize(photo) is { } size
                    ? EditGeometry.DecodeLongSide(size.Width, size.Height, recipe, maxLongSide)
                    : maxLongSide;
                var source = loader.Load(photo.FilePath, decodeSide ?? int.MaxValue);
                cancellationToken.ThrowIfCancellationRequested();
                return EditRenderer.Render(source, recipe, local.Masks, maxLongSide, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalMasks> LoadMasksAsync(Photo photo, EditRecipe recipe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(recipe);
        if (!recipe.HasLocalAdjustments)
        {
            return LocalMasks.None;
        }

        var found = new Dictionary<Guid, MaskImage>();
        var unavailable = new List<(Guid, string)>();
        foreach (var adjustment in recipe.LocalAdjustments)
        {
            if (adjustment.HasNoEffect)
            {
                continue; // nothing to draw, so no mask is needed
            }

            var outcome = await masks.GetAsync(photo, adjustment.Mask, cancellationToken).ConfigureAwait(false);
            if (outcome.Mask is { } mask)
            {
                found[adjustment.Id] = mask;
            }
            else
            {
                unavailable.Add((adjustment.Id, outcome.Summary));
            }
        }

        return new LocalMasks(found, unavailable);
    }

    /// <summary>Stored size as displayed (EXIF orientations 5–8 swap width and height), or null if unknown.</summary>
    public static (int Width, int Height)? OrientedSize(Photo photo) =>
        photo.Width is { } w && photo.Height is { } h
            ? photo.Orientation is >= 5 and <= 8 ? (h, w) : (w, h)
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo {PhotoId}: local adjustment {AdjustmentId} left out of the render: {Reason}")]
    private partial void LogMaskLeftOut(Guid photoId, Guid adjustmentId, string reason);
}
