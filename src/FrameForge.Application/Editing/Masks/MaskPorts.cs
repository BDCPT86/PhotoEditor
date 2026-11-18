using FrameForge.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// The result of making a mask: the mask and a short description of what was found ("2 people"), or why there is
/// none ("No people found"). A missing mask is a normal outcome, not an error.
/// </summary>
public sealed record MaskGeneration(MaskImage? Mask, string Summary)
{
    public bool HasMask => Mask is not null;

    public static MaskGeneration Found(MaskImage mask, string summary) => new(mask, summary);

    public static MaskGeneration NotFound(string reason) => new(null, reason);
}

/// <summary>Makes one kind of mask from the upright photo at analysis resolution.</summary>
public interface IMaskGenerator
{
    MaskKind Kind { get; }

    /// <summary>
    /// What makes the mask and its version (stored in <see cref="MaskReference.Source"/> and in the cache key), so a
    /// different model or rule set never reuses an old mask.
    /// </summary>
    string Source { get; }

    /// <summary>Null when the generator can run; otherwise why not (for example, a model is not installed).</summary>
    string? UnavailableReason { get; }

    /// <param name="points">Clicks in normalised coordinates of <paramref name="image"/>.</param>
    MaskGeneration Generate(AnalysisImage image, IReadOnlyList<MaskPoint> points, CancellationToken cancellationToken);
}

/// <summary>A cached mask with the description it was made with.</summary>
public sealed record CachedMask(MaskImage Mask, string Summary);

/// <summary>Disk cache of computed masks (derived data, safe to delete), keyed by <see cref="MaskKey"/>.</summary>
public interface IMaskCache
{
    /// <summary>The cached mask, or null when it is missing or unreadable (an unreadable entry is removed).</summary>
    CachedMask? TryGet(string key);

    /// <exception cref="IOException">The cache could not be written.</exception>
    void Save(string key, CachedMask mask);
}

/// <summary>
/// What Develop gets for a mask reference: the mask, or the reason it is unavailable. <see cref="Source"/> is what
/// made it, which can be newer than the reference's when the model was updated.
/// </summary>
public sealed record MaskOutcome
{
    public MaskImage? Mask { get; init; }

    public required string Source { get; init; }

    /// <summary>What was found, or why there is no mask.</summary>
    public required string Summary { get; init; }

    public bool FromCache { get; init; }

    public bool HasMask => Mask is not null;

    public static MaskOutcome Unavailable(string source, string reason) => new() { Source = source, Summary = reason };
}

/// <summary>Computes masks on demand, one at a time, cached on disk. Never throws for model or file problems.</summary>
public interface IMaskService
{
    /// <summary>The generator's current source for a kind, or null when no generator handles it.</summary>
    string? CurrentSource(MaskKind kind);

    /// <summary>Null when masks of this kind can be made; otherwise why not.</summary>
    string? UnavailableReason(MaskKind kind);

    /// <exception cref="OperationCanceledException">Cancelled; nothing is cached.</exception>
    Task<MaskOutcome> GetAsync(Photo photo, MaskReference reference, CancellationToken cancellationToken);
}
