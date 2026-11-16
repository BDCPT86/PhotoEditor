using FrameForge.Analysis;

namespace FrameForge.Application.Ai;

public enum ModelState
{
    NotInstalled,
    Installed,

    /// <summary>A file is present but its size does not match the catalogue; it will not be used.</summary>
    Invalid,
}

public sealed record ModelStatus(ModelDescriptor Model, ModelState State, string? FilePath);

/// <summary>Record that the user saw the model's size, source and licence and agreed to download it.</summary>
public sealed record ModelDownloadConsent(string ModelId, DateTime AcceptedUtc);

/// <summary>Where model files live, and how they get there. Never downloads on its own initiative.</summary>
public interface IModelStore
{
    ModelStatus GetStatus(ModelDescriptor model);

    /// <summary>Downloads from the catalogue's pinned URL and verifies the SHA-256 before installing.</summary>
    /// <exception cref="ModelVerificationException">The downloaded file does not match the catalogue.</exception>
    Task<string> DownloadAsync(ModelDescriptor model, ModelDownloadConsent consent, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Copies a user-supplied file into the store after verifying its SHA-256 (offline installs).</summary>
    /// <exception cref="ModelVerificationException">The file is not the catalogued model.</exception>
    Task<string> ImportAsync(ModelDescriptor model, string sourceFile, CancellationToken cancellationToken);

    void Remove(ModelDescriptor model);
}

public sealed class ModelVerificationException : Exception
{
    public ModelVerificationException()
    {
    }

    public ModelVerificationException(string message)
        : base(message)
    {
    }

    public ModelVerificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <param name="X">Left edge, 0–1 of image width.</param>
/// <param name="Y">Top edge, 0–1 of image height.</param>
/// <param name="Score">Detector confidence 0–1 (not a calibrated probability).</param>
/// <param name="Landmarks">Five (x, y) points, normalised like the box: right eye, left eye, nose, mouth right, mouth left.</param>
public sealed record DetectedFace(float X, float Y, float Width, float Height, float Score, float[] Landmarks);

public interface IFaceDetector
{
    string VersionTag { get; }

    /// <summary>Faces with score at or above <paramref name="minScore"/>, after non-maximum suppression.</summary>
    IReadOnlyList<DetectedFace> Detect(AnalysisImage image, float minScore);
}

public interface IImageEmbedder
{
    string VersionTag { get; }

    /// <summary>L2-normalised embedding (cosine similarity = dot product).</summary>
    float[] Embed(AnalysisImage image);
}

/// <summary>
/// Gives access to installed models. Returns null when a model is not installed or failed to load (the reason is in
/// <see cref="LastError"/>); callers must treat AI as optional.
/// </summary>
public interface IAiRuntime
{
    string? LastError { get; }

    IFaceDetector? GetFaceDetector();

    IImageEmbedder? GetImageEmbedder();

    /// <summary>Drops loaded models (after install/remove), so the next request reloads from disk.</summary>
    void Reset();
}
