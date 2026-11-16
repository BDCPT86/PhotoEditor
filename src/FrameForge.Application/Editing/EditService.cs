using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Editing;

public sealed record BatchProgress(int Total, int Completed);

/// <summary>What a batch changed, so it can be undone exactly.</summary>
public sealed record BatchEditResult(IReadOnlyList<(Guid PhotoId, EditRecipe Before, EditRecipe After)> Changes, bool WasCancelled);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored edit for photo {PhotoId} was ignored: {Reason}")]
    private partial void LogRejected(Guid photoId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Preset {Preset} applied to {Count} photo(s)")]
    private partial void LogBatchApplied(string preset, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Preset {Preset} cancelled after {Done} of {Total} photo(s); those stay applied")]
    private partial void LogBatchCancelled(string preset, int done, int total);
}

/// <summary>Decodes a photo at the size a render needs and renders a recipe onto it.</summary>
public interface IEditRenderService
{
    /// <summary>Decodes the original (read-only) at most <paramref name="maxLongSide"/> on its longest side, upright.</summary>
    Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken);

    /// <summary>Decodes at the size the output needs, then renders. Null = full resolution.</summary>
    Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken);
}

public sealed class EditRenderService(IAnalysisImageLoader loader) : IEditRenderService
{
    public Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return Task.Run(() => loader.Load(photo.FilePath, maxLongSide ?? int.MaxValue), cancellationToken);
    }

    public Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(recipe);
        return Task.Run(
            () =>
            {
                var decodeSide = OrientedSize(photo) is { } size
                    ? EditGeometry.DecodeLongSide(size.Width, size.Height, recipe, maxLongSide)
                    : maxLongSide;
                var source = loader.Load(photo.FilePath, decodeSide ?? int.MaxValue);
                cancellationToken.ThrowIfCancellationRequested();
                return EditRenderer.Render(source, recipe, maxLongSide, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Stored size as displayed (EXIF orientations 5–8 swap width and height), or null if unknown.</summary>
    internal static (int Width, int Height)? OrientedSize(Photo photo) =>
        photo.Width is { } w && photo.Height is { } h
            ? photo.Orientation is >= 5 and <= 8 ? (h, w) : (w, h)
            : null;
}
