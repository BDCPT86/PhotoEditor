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

/// <summary>A box in normalised image coordinates (0–1): left, top, right, bottom.</summary>
public readonly record struct NormalisedBox(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>A person found by the person detector. <see cref="Score"/> is the detector's confidence (0–1, not calibrated).</summary>
public sealed record DetectedPerson(NormalisedBox Box, float Score);

/// <summary>Finds people (whole bodies, not faces) for prompting the subject mask.</summary>
public interface IPersonDetector
{
    string VersionTag { get; }

    /// <summary>People with a score at or above <paramref name="minScore"/>, after non-maximum suppression.</summary>
    IReadOnlyList<DetectedPerson> Detect(AnalysisImage image, float minScore, CancellationToken cancellationToken);
}

/// <summary>
/// One region to segment: an optional box and optional clicks (normalised like the box; <c>Include</c> = part of the
/// region). At least one of them must be given.
/// </summary>
public sealed record SegmentPrompt(NormalisedBox? Box, IReadOnlyList<Core.Editing.MaskPoint> Points);

/// <summary>A promptable segmentation model: the image is encoded once, then each prompt gives one mask.</summary>
public interface IPromptSegmenter
{
    string VersionTag { get; }

    /// <summary>One mask per prompt, at the image's size, each 0 or 255.</summary>
    IReadOnlyList<Rendering.MaskImage> Segment(AnalysisImage image, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken);
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

    /// <summary>The person detector for subject masks (Milestone 08), or null.</summary>
    IPersonDetector? GetPersonDetector();

    /// <summary>The prompted segmenter for subject masks (Milestone 08; needs both of its files), or null.</summary>
    IPromptSegmenter? GetPromptSegmenter();

    /// <summary>Drops loaded models (after install/remove), so the next request reloads from disk.</summary>
    void Reset();
}
