using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Ai;

/// <summary>A detected face plus the deterministic sharpness of its region (null when the face is too small to judge).</summary>
public sealed record FaceFinding(float X, float Y, float Width, float Height, float Score, double? Sharpness);

public sealed record FacesResult(IReadOnlyList<FaceFinding> Faces);

/// <param name="Vector">Base64 of the little-endian float32 values (compact JSON).</param>
public sealed record EmbeddingResult(int Dimensions, string Vector)
{
    public float[] ToArray()
    {
        var bytes = Convert.FromBase64String(Vector);
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    public static EmbeddingResult From(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return new EmbeddingResult(values.Length, Convert.ToBase64String(bytes));
    }
}

/// <summary>AI outputs for one photo. Either may be missing (capability off, model absent, stale, or failed).</summary>
public sealed record AiPhotoAnalysis
{
    public static AiPhotoAnalysis Empty { get; } = new();

    public FacesResult? Faces { get; init; }

    public float[]? Embedding { get; init; }
}

/// <summary>Which AI capabilities to run. Each requires its model to be installed.</summary>
public sealed record AiCapabilities(bool Faces, bool Scenes)
{
    public static AiCapabilities None { get; } = new(false, false);

    public bool Any => Faces || Scenes;
}

public sealed record AiAnalysisOptions
{
    /// <summary>Photos processed concurrently. Each model session also limits its own threads.</summary>
    public int MaxParallelism { get; init; } = 2;

    public int BatchSize { get; init; } = 25;

    /// <summary>Faces below this detector score are not stored (interpretation thresholds are applied later).</summary>
    public float StoredFaceScoreFloor { get; init; } = 0.5f;
}

public interface IAiAnalysisService
{
    Task<IReadOnlyDictionary<Guid, AiPhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, AiCapabilities capabilities, AnalysisSettings settings, CancellationToken cancellationToken = default);

    Task<AnalysisRunSummary> AnalyzeAsync(IReadOnlyList<Photo> photos, AiCapabilities capabilities, AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Runs optional AI models with bounded concurrency. Results are cached like deterministic analysis, keyed by the
/// model's version tag (id, version, file checksum) plus settings and source identity. This service never touches
/// culling decisions. Model failures disable the capability for the run and are reported, never thrown at the UI.
/// </summary>
public sealed partial class AiAnalysisService(
    IAnalysisResultRepository repository,
    IAnalysisImageLoader imageLoader,
    IAiRuntime runtime,
    AiAnalysisOptions options,
    TimeProvider timeProvider,
    ILogger<AiAnalysisService> logger) : IAiAnalysisService
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<IReadOnlyDictionary<Guid, AiPhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, AiCapabilities capabilities, AnalysisSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var result = new Dictionary<Guid, AiPhotoAnalysis>();
        if (!capabilities.Any)
        {
            return result;
        }

        var tags = CurrentTags(capabilities);
        var rows = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToLookup(r => r.PhotoId);
        foreach (var photo in photos)
        {
            var analysis = AiPhotoAnalysis.Empty;
            foreach (var row in rows[photo.Id])
            {
                if (IsValid(row, photo, tags, settings))
                {
                    analysis = Apply(analysis, row);
                }
            }

            if (analysis != AiPhotoAnalysis.Empty)
            {
                result[photo.Id] = analysis;
            }
        }

        return result;
    }

    public async Task<AnalysisRunSummary> AnalyzeAsync(IReadOnlyList<Photo> photos, AiCapabilities capabilities, AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var started = timeProvider.GetTimestamp();
        var detector = capabilities.Faces ? runtime.GetFaceDetector() : null;
        var embedder = capabilities.Scenes ? runtime.GetImageEmbedder() : null;
        if (detector is null && embedder is null)
        {
            LogNothingToRun(runtime.LastError ?? "no AI capability enabled with an installed model");
            return new AnalysisRunSummary { NotEligible = photos.Count, Duration = timeProvider.GetElapsedTime(started) };
        }

        var tags = new Dictionary<AnalysisKind, string>();
        if (detector is not null)
        {
            tags[AnalysisKind.Faces] = detector.VersionTag;
        }

        if (embedder is not null)
        {
            tags[AnalysisKind.SceneEmbedding] = embedder.VersionTag;
        }

        var eligible = photos.Where(p => p.Status == ImportStatus.Imported).ToList();
        int analysed = 0, failed = 0, processed = 0;
        var cancelled = false;
        var work = new List<(Photo Photo, AnalysisKind[] Needed)>();

        try
        {
            var rows = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToLookup(r => r.PhotoId);
            foreach (var photo in eligible)
            {
                var have = rows[photo.Id].Where(r => IsValid(r, photo, tags, settings)).Select(r => r.Kind).ToHashSet();
                var needed = tags.Keys.Where(k => !have.Contains(k)).ToArray();
                if (needed.Length > 0)
                {
                    work.Add((photo, needed));
                }
            }

            LogStarting(work.Count, eligible.Count - work.Count, tags.Values);
            progress?.Report(new AnalysisProgress(work.Count, 0, 0));

            foreach (var batch in work.Chunk(options.BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = new List<StoredAnalysis>?[batch.Length];
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
                                    results[i] = AnalyzeOne(photo, needed, detector, embedder, tags, settings, nowUtc);
                                    Interlocked.Increment(ref analysed);
                                }
                                catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
                                {
                                    Interlocked.Increment(ref failed);
                                    LogPhotoFailed(ex, photo.FilePath);
                                }
#pragma warning disable CA1031 // Model/runtime errors must not stop the run or reach the UI; they are logged per photo.
                                catch (Exception ex)
#pragma warning restore CA1031
                                {
                                    Interlocked.Increment(ref failed);
                                    LogInferenceFailed(ex, photo.FilePath);
                                }

                                var done = Interlocked.Increment(ref processed);
                                progress?.Report(new AnalysisProgress(work.Count, done, Volatile.Read(ref failed)));
                            }),
                        CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    var save = results.Where(r => r is not null).SelectMany(r => r!).ToList();
                    if (save.Count > 0)
                    {
                        await repository.SaveAsync(save, CancellationToken.None).ConfigureAwait(false);
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

    /// <summary>Faces need the image at the analysis resolution; the face region's sharpness reuses the deterministic metric.</summary>
    private List<StoredAnalysis> AnalyzeOne(
        Photo photo,
        AnalysisKind[] needed,
        IFaceDetector? detector,
        IImageEmbedder? embedder,
        Dictionary<AnalysisKind, string> tags,
        AnalysisSettings settings,
        DateTime nowUtc)
    {
        var image = imageLoader.Load(photo.FilePath, settings.MaxPixelSize);
        var rows = new List<StoredAnalysis>(needed.Length);

        if (detector is not null && needed.Contains(AnalysisKind.Faces))
        {
            var faces = detector.Detect(image, options.StoredFaceScoreFloor)
                .Select(f => new FaceFinding(f.X, f.Y, f.Width, f.Height, f.Score, FaceSharpness(image, f)))
                .ToList();
            rows.Add(Row(photo, AnalysisKind.Faces, tags, settings, new FacesResult(faces), nowUtc));
        }

        if (embedder is not null && needed.Contains(AnalysisKind.SceneEmbedding))
        {
            rows.Add(Row(photo, AnalysisKind.SceneEmbedding, tags, settings, EmbeddingResult.From(embedder.Embed(image)), nowUtc));
        }

        return rows;
    }

    /// <summary>Smallest face (shorter side, analysis pixels) whose sharpness is measured; smaller crops are mostly noise.</summary>
    public const int MinFacePixelsForSharpness = 16;

    /// <summary>Sharpness of the face region using the deterministic Laplacian measure; null if the face is too small to judge.</summary>
    public static double? FaceSharpness(AnalysisImage image, DetectedFace face)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(face);
        if (Math.Min(face.Width * image.Width, face.Height * image.Height) < MinFacePixelsForSharpness)
        {
            return null;
        }

        var crop = image.Crop(face.X, face.Y, face.Width, face.Height);
        return crop is null ? null : SharpnessAnalyzer.Analyze(crop, 1).Score;
    }

    private Dictionary<AnalysisKind, string> CurrentTags(AiCapabilities capabilities)
    {
        var tags = new Dictionary<AnalysisKind, string>();
        if (capabilities.Faces)
        {
            tags[AnalysisKind.Faces] = runtime.GetFaceDetector()?.VersionTag ?? AiModelCatalog.FaceDetector.VersionTag;
        }

        if (capabilities.Scenes)
        {
            tags[AnalysisKind.SceneEmbedding] = runtime.GetImageEmbedder()?.VersionTag ?? AiModelCatalog.SceneEmbedder.VersionTag;
        }

        return tags;
    }

    private static string SettingsHash(AnalysisKind kind, AnalysisSettings settings, AiAnalysisOptions options) =>
        kind == AnalysisKind.Faces
            ? $"faces;max={settings.MaxPixelSize};floor={options.StoredFaceScoreFloor:0.00}"
            : $"embed;max={settings.MaxPixelSize}";

    private bool IsValid(StoredAnalysis row, Photo photo, Dictionary<AnalysisKind, string> tags, AnalysisSettings settings) =>
        tags.TryGetValue(row.Kind, out var tag)
        && row.AlgorithmVersion == tag
        && row.SettingsHash == SettingsHash(row.Kind, settings, options)
        && row.SourceSizeBytes == photo.FileSizeBytes
        && row.SourceModifiedUtc == photo.LastModifiedUtc;

    private StoredAnalysis Row(Photo photo, AnalysisKind kind, Dictionary<AnalysisKind, string> tags, AnalysisSettings settings, object value, DateTime nowUtc) =>
        new(photo.Id, kind, tags[kind], SettingsHash(kind, settings, options), photo.FileSizeBytes, photo.LastModifiedUtc,
            JsonSerializer.Serialize(value, value.GetType(), Json), nowUtc);

    private static AiPhotoAnalysis Apply(AiPhotoAnalysis analysis, StoredAnalysis row)
    {
        try
        {
            return row.Kind switch
            {
                AnalysisKind.Faces => analysis with { Faces = JsonSerializer.Deserialize<FacesResult>(row.ValueJson, Json) },
                AnalysisKind.SceneEmbedding => analysis with { Embedding = JsonSerializer.Deserialize<EmbeddingResult>(row.ValueJson, Json)?.ToArray() },
                _ => analysis,
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return analysis;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI analysis skipped: {Reason}")]
    private partial void LogNothingToRun(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI analysis started: {Pending} photo(s), {Cached} cached, models {Models}")]
    private partial void LogStarting(int pending, int cached, IEnumerable<string> models);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo could not be read for AI analysis: {FilePath}")]
    private partial void LogPhotoFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Model inference failed for {FilePath}")]
    private partial void LogInferenceFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI analysis finished: analysed={Analysed} cached={Cached} failed={Failed} cancelled={Cancelled} in {Seconds:0.0}s ({MsPerPhoto:0.0} ms/photo)")]
    private partial void LogFinished(int analysed, int cached, int failed, bool cancelled, double seconds, double msPerPhoto);
}
