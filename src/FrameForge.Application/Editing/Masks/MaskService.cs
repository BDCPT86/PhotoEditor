using FrameForge.Application.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// Computes masks on demand (never for the whole library), one at a time, and caches them on disk. A missing model,
/// an unreadable photo or a model failure gives an unavailable outcome with the reason, so Develop keeps working.
/// </summary>
public sealed partial class MaskService(
    IEnumerable<IMaskGenerator> generators,
    IAnalysisImageLoader loader,
    IMaskCache cache,
    ILogger<MaskService> logger) : IMaskService, IDisposable
{
    /// <summary>
    /// Long side of the photo masks are computed and cached at. Matches MobileSAM's input size; the renderer refines
    /// and upsamples the mask to the output size (step B4).
    /// </summary>
    public const int AnalysisLongSide = 1024;

    private readonly Dictionary<MaskKind, IMaskGenerator> _generators = generators.ToDictionary(g => g.Kind);

    // Bounded concurrency: masks are heavy (seconds of CPU and about 100 MB of models), so one runs at a time.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The last photo decoded for a mask, so refining it with clicks reuses the same image (and the models' work on it).
    private (string Identity, FrameForge.Analysis.AnalysisImage Image)? _lastImage;

    public string? CurrentSource(MaskKind kind) => _generators.TryGetValue(kind, out var g) ? g.Source : null;

    /// <remarks>May load models on first use (well under a second); call it off the UI thread.</remarks>
    public string? UnavailableReason(MaskKind kind) =>
        _generators.TryGetValue(kind, out var g) ? g.UnavailableReason : $"{kind} masks are not supported.";

    public MaskOutcome? TryGetCached(Photo photo, MaskReference reference)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(reference);
        if (!_generators.TryGetValue(reference.Kind, out var generator))
        {
            return null;
        }

        return cache.TryGet(MaskKey.Compute(photo, generator.Source, reference.Points, AnalysisLongSide)) is { } cached
            ? new MaskOutcome { Mask = cached.Mask, Source = generator.Source, Summary = cached.Summary, FromCache = true }
            : null;
    }

    public async Task<MaskOutcome> GetAsync(Photo photo, MaskReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(reference);

        if (!_generators.TryGetValue(reference.Kind, out var generator))
        {
            return MaskOutcome.Unavailable(reference.Source, $"{reference.Kind} masks are not supported.");
        }

        if (await Task.Run(() => generator.UnavailableReason, cancellationToken).ConfigureAwait(false) is { } notReady)
        {
            return MaskOutcome.Unavailable(generator.Source, notReady);
        }

        var key = MaskKey.Compute(photo, generator.Source, reference.Points, AnalysisLongSide);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.TryGet(key) is { } cached)
            {
                return new MaskOutcome { Mask = cached.Mask, Source = generator.Source, Summary = cached.Summary, FromCache = true };
            }

            return await Task.Run(() => Compute(photo, reference, generator, key, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private MaskOutcome Compute(Photo photo, MaskReference reference, IMaskGenerator generator, string key, CancellationToken cancellationToken)
    {
        FrameForge.Analysis.AnalysisImage image;
        var identity = MaskKey.Compute(photo, "image", [], AnalysisLongSide);
        try
        {
            if (_lastImage is { } last && last.Identity == identity)
            {
                image = last.Image;
            }
            else
            {
                image = loader.Load(photo.FilePath, AnalysisLongSide);
                _lastImage = (identity, image);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PhotoReadException)
        {
            LogPhotoUnreadable(ex, photo.Id);
            return MaskOutcome.Unavailable(generator.Source, $"The photo could not be read: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        MaskGeneration result;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            result = generator.Generate(image, reference.Points, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogGenerationFailed(ex, reference.Kind, photo.Id, generator.Source);
            return MaskOutcome.Unavailable(generator.Source, $"The {reference.Kind.ToString().ToLowerInvariant()} mask could not be made: {ex.Message}");
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        LogComputed(reference.Kind, photo.Id, elapsed.TotalMilliseconds, result.Summary);

        if (result.Mask is null)
        {
            return MaskOutcome.Unavailable(generator.Source, result.Summary);
        }

        try
        {
            cache.Save(key, new CachedMask(result.Mask, result.Summary));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The mask is still usable; it is computed again next time.
            LogCacheWriteFailed(ex, photo.Id);
        }

        return new MaskOutcome { Mask = result.Mask, Source = generator.Source, Summary = result.Summary };
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo {PhotoId} could not be read for a mask")]
    private partial void LogPhotoUnreadable(Exception exception, Guid photoId);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} mask failed for photo {PhotoId} ({Source})")]
    private partial void LogGenerationFailed(Exception exception, MaskKind kind, Guid photoId, string source);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} mask for photo {PhotoId} computed in {Ms:F0} ms: {Summary}")]
    private partial void LogComputed(MaskKind kind, Guid photoId, double ms, string summary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mask for photo {PhotoId} could not be cached")]
    private partial void LogCacheWriteFailed(Exception exception, Guid photoId);
}
