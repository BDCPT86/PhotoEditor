using FrameForge.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Analysis;

public sealed record PhotoAnalysisOptions
{
    /// <summary>Photos analysed concurrently (decode + metrics + file hash).</summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>Photos analysed before results are written in one transaction.</summary>
    public int BatchSize { get; init; } = 50;

    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Runs deterministic analyses with bounded concurrency off the calling thread. Each photo is decoded once (at the
/// analysis resolution) for all image metrics; the file hash streams the original read-only.
/// </summary>
public sealed partial class PhotoAnalysisService(
    IAnalysisResultRepository repository,
    IAnalysisImageLoader imageLoader,
    IFileHasher fileHasher,
    PhotoAnalysisOptions options,
    TimeProvider timeProvider,
    ILogger<PhotoAnalysisService> logger) : IPhotoAnalysisService
{
    public async Task<IReadOnlyDictionary<Guid, PhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, AnalysisSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(settings);

        var stored = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var byPhoto = stored.ToLookup(s => s.PhotoId);
        var result = new Dictionary<Guid, PhotoAnalysis>(photos.Count);
        foreach (var photo in photos)
        {
            var analysis = PhotoAnalysis.Empty;
            foreach (var row in byPhoto[photo.Id])
            {
                if (AnalysisCache.IsValid(row, photo, settings))
                {
                    analysis = AnalysisCache.Apply(analysis, row);
                }
            }

            if (analysis != PhotoAnalysis.Empty)
            {
                result[photo.Id] = analysis;
            }
        }

        return result;
    }

    public async Task<AnalysisRunSummary> AnalyzeAsync(
        IReadOnlyList<Photo> photos,
        AnalysisSettings settings,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(settings);

        var started = timeProvider.GetTimestamp();
        var eligible = photos.Where(p => p.Status == ImportStatus.Imported).ToList();
        int analysed = 0, failed = 0, processed = 0;
        var cancelled = false;
        var work = new List<(Photo Photo, AnalysisKind[] Needed)>();

        try
        {
            var stored = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var byPhoto = stored.ToLookup(s => s.PhotoId);

            foreach (var photo in eligible)
            {
                var have = byPhoto[photo.Id].Where(s => AnalysisCache.IsValid(s, photo, settings)).Select(s => s.Kind).ToHashSet();
                var needed = AnalysisCache.AllKinds.Where(k => !have.Contains(k)).ToArray();
                if (needed.Length > 0)
                {
                    work.Add((photo, needed));
                }
            }

            LogStarting(work.Count, eligible.Count - work.Count, settings.MaxPixelSize);
            progress?.Report(new AnalysisProgress(work.Count, 0, 0));
            var lastReport = timeProvider.GetTimestamp();
            var reportLock = new Lock();

            foreach (var batch in work.Chunk(options.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = new List<StoredAnalysis>[batch.Length];
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                try
                {
                    await Task.Run(
                        () => Parallel.For(
                            0,
                            batch.Length,
                            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = cancellationToken },
                            i =>
                            {
                                var (photo, needed) = batch[i];
                                try
                                {
                                    results[i] = AnalyzeOne(photo, needed, settings, nowUtc, cancellationToken);
                                    Interlocked.Increment(ref analysed);
                                }
                                catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
                                {
                                    Interlocked.Increment(ref failed);
                                    LogPhotoFailed(ex, photo.FilePath);
                                }

                                var done = Interlocked.Increment(ref processed);
                                lock (reportLock)
                                {
                                    var now = timeProvider.GetTimestamp();
                                    if (done == work.Count || timeProvider.GetElapsedTime(lastReport, now) >= options.ProgressInterval)
                                    {
                                        lastReport = now;
                                        progress?.Report(new AnalysisProgress(work.Count, done, Volatile.Read(ref failed)));
                                    }
                                }
                            }),
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    // Persist what finished, even on cancellation, so the next run resumes.
                    var rows = results.Where(r => r is not null).SelectMany(r => r).ToList();
                    if (rows.Count > 0)
                    {
                        await repository.SaveAsync(rows, CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        var summary = new AnalysisRunSummary
        {
            Analysed = analysed,
            Cached = eligible.Count - work.Count,
            Failed = failed,
            NotEligible = photos.Count - eligible.Count,
            WasCancelled = cancelled,
            Duration = timeProvider.GetElapsedTime(started),
        };
        LogFinished(summary.Analysed, summary.Cached, summary.Failed, summary.WasCancelled, summary.Duration.TotalSeconds,
            summary.Analysed == 0 ? 0 : summary.Duration.TotalMilliseconds / summary.Analysed);
        return summary;
    }

    private List<StoredAnalysis> AnalyzeOne(Photo photo, AnalysisKind[] needed, AnalysisSettings settings, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var rows = new List<StoredAnalysis>(needed.Length);
        if (needed.Any(k => k != AnalysisKind.ContentHash))
        {
            var image = imageLoader.Load(photo.FilePath, settings.MaxPixelSize);
            foreach (var kind in needed)
            {
                object? value = kind switch
                {
                    AnalysisKind.Sharpness => SharpnessAnalyzer.Analyze(image, settings.SharpnessGrid),
                    AnalysisKind.Exposure => ExposureAnalyzer.Analyze(image),
                    AnalysisKind.PerceptualHash => PerceptualHasher.Compute(image),
                    _ => null,
                };

                if (value is not null)
                {
                    rows.Add(AnalysisCache.Create(photo, kind, settings, value, nowUtc));
                }
            }
        }

        if (needed.Contains(AnalysisKind.ContentHash))
        {
            var hash = fileHasher.ComputeSha256(photo.FilePath, cancellationToken);
            rows.Add(AnalysisCache.Create(photo, AnalysisKind.ContentHash, settings, hash, nowUtc));
        }

        return rows;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis started: {Pending} photo(s) to analyse, {Cached} cached, at {MaxPixelSize}px")]
    private partial void LogStarting(int pending, int cached, int maxPixelSize);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo could not be analysed: {FilePath}")]
    private partial void LogPhotoFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis finished: analysed={Analysed} cached={Cached} failed={Failed} cancelled={Cancelled} in {Seconds:0.0}s ({MsPerPhoto:0.0} ms/photo)")]
    private partial void LogFinished(int analysed, int cached, int failed, bool cancelled, double seconds, double msPerPhoto);
}
