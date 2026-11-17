using FrameForge.Application.Ai;
using FrameForge.Core.Editing;

namespace FrameForge.Application.Editing.Auto;

/// <summary>A line to draw over a suggestion's preview, in normalised output coordinates (0–1).</summary>
public sealed record GuideLine(double X1, double Y1, double X2, double Y2);

/// <summary>One option the user can pick. <see cref="Recipe"/> is the whole recipe after accepting it.</summary>
public sealed record AutoSuggestion(string Name, EditRecipe Recipe, string Reason, GuideLine? Guide = null);

/// <summary>
/// The options an Auto tool offers, or why it has none. Options are never applied by the tool itself: the user
/// picks one (one undo step) or dismisses them.
/// </summary>
public sealed record AutoSuggestions(string Title, IReadOnlyList<AutoSuggestion> Options, string? Unavailable = null)
{
    public bool HasOptions => Options.Count > 0;

    public static AutoSuggestions None(string title, string reason) => new(title, [], reason);

    /// <summary>The option with this name (case-insensitive), for batch runs that choose a variant in advance.</summary>
    public AutoSuggestion? Named(string name) =>
        Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Face boxes for a photo, from the optional face analysis (empty when not analysed or not enabled).</summary>
public interface IFaceLookup
{
    /// <summary>Detector score a face needs to count (the user's AI threshold).</summary>
    double FaceMinScore { get; }

    /// <summary>True when faces were analysed for this photo (even if none were found).</summary>
    bool HasFaceData(Guid photoId);

    IReadOnlyList<FaceFinding> FacesOf(Guid photoId);
}

/// <summary>A lookup with no face data, for when face analysis is unavailable.</summary>
public sealed class NoFaces : IFaceLookup
{
    public static NoFaces Instance { get; } = new();

    public double FaceMinScore => AiThresholds.Default.FaceMinScore;

    public bool HasFaceData(Guid photoId) => false;

    public IReadOnlyList<FaceFinding> FacesOf(Guid photoId) => [];
}
