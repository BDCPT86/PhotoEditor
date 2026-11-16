using FrameForge.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Analysis;

public enum AnalysisKind
{
    ContentHash = 0,
    Sharpness = 1,
    Exposure = 2,
    PerceptualHash = 3,
}

/// <summary>
/// One persisted analysis value with everything that determines whether it is still valid: the algorithm version,
/// the settings hash, and the source file's identity (size + modified time) at the time it was computed.
/// </summary>
public sealed record StoredAnalysis(
    Guid PhotoId,
    AnalysisKind Kind,
    string AlgorithmVersion,
    string SettingsHash,
    long SourceSizeBytes,
    DateTime SourceModifiedUtc,
    string ValueJson,
    DateTime CreatedUtc);

/// <summary>Typed analysis values for one photo. Any value may be missing (not analysed, stale, or failed).</summary>
public sealed record PhotoAnalysis
{
    public static PhotoAnalysis Empty { get; } = new();

    public string? ContentHash { get; init; }

    public SharpnessResult? Sharpness { get; init; }

    public ExposureResult? Exposure { get; init; }

    public ulong? PerceptualHash { get; init; }

    public bool IsComplete => ContentHash is not null && Sharpness is not null && Exposure is not null && PerceptualHash is not null;
}

public sealed record AnalysisProgress(int Total, int Processed, int Failed);

public sealed record AnalysisRunSummary
{
    /// <summary>Photos that were (re)analysed in this run.</summary>
    public int Analysed { get; init; }

    /// <summary>Photos whose stored results were still valid.</summary>
    public int Cached { get; init; }

    /// <summary>Photos that could not be read.</summary>
    public int Failed { get; init; }

    /// <summary>Missing or unreadable photos that were not attempted.</summary>
    public int NotEligible { get; init; }

    public bool WasCancelled { get; init; }

    public TimeSpan Duration { get; init; }
}

public interface IAnalysisResultRepository
{
    Task<IReadOnlyList<StoredAnalysis>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces results, matched by photo id and kind.</summary>
    Task SaveAsync(IReadOnlyCollection<StoredAnalysis> results, CancellationToken cancellationToken = default);
}

/// <summary>Decodes a photo for analysis (downscaled, oriented). Never modifies the source.</summary>
public interface IAnalysisImageLoader
{
    /// <exception cref="Photos.PhotoReadException">The file is not a readable image.</exception>
    /// <exception cref="IOException">The file could not be opened.</exception>
    AnalysisImage Load(string path, int maxPixelSize);
}

public interface IFileHasher
{
    /// <returns>Lower-case hex SHA-256 of the file's bytes.</returns>
    string ComputeSha256(string path, CancellationToken cancellationToken);
}

public interface IPhotoAnalysisService
{
    /// <summary>Loads stored results that are still valid for the given photos and settings.</summary>
    Task<IReadOnlyDictionary<Guid, PhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, AnalysisSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Analyses photos whose results are missing or stale. Cancellation keeps finished work.</summary>
    Task<AnalysisRunSummary> AnalyzeAsync(IReadOnlyList<Photo> photos, AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
}
