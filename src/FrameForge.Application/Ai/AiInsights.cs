using System.Globalization;
using FrameForge.Application.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Ai;

public sealed record AiThresholds
{
    public static AiThresholds Default { get; } = new();

    public double FaceMinScore { get; init; } = 0.6;

    public double FaceSoftBelow { get; init; } = 40;

    public double SceneSimilarityMin { get; init; } = 0.9;

    /// <summary>0 = no time limit (shared with the deterministic burst window).</summary>
    public int BurstWindowSeconds { get; init; } = 10;
}

public enum SuggestionConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// AI findings for one photo. Advisory only: nothing here is ever written to a culling decision.
/// </summary>
public sealed record AiInsight
{
    public static AiInsight None { get; } = new();

    public bool HasFaces => FaceCount > 0;

    public int FaceCount { get; init; }

    /// <summary>All counted faces are below the "soft" threshold.</summary>
    public bool FacesSoft { get; init; }

    public string? SceneGroupKey { get; init; }

    public int SceneGroupSize { get; init; }

    /// <summary>Suggested as the photo to keep within its group (scene group, else similar-photo group).</summary>
    public bool IsSuggestedPick { get; init; }

    public SuggestionConfidence? PickConfidence { get; init; }

    public IReadOnlyList<InsightLine> Details { get; init; } = [];
}

/// <summary>
/// Turns stored AI outputs into advisory insights. Pure and deterministic. Quality has not been evaluated on a
/// human-reviewed set; every explanation says so.
/// </summary>
public static class AiInsights
{
    public const string Disclaimer = "AI suggestion — advisory only, not yet evaluated on a human-reviewed set. Your decisions are never changed.";

    public static IReadOnlyDictionary<Guid, AiInsight> Build(
        IReadOnlyList<Photo> photos,
        IReadOnlyDictionary<Guid, AiPhotoAnalysis> ai,
        IReadOnlyDictionary<Guid, PhotoAnalysis> deterministic,
        IReadOnlyDictionary<Guid, PhotoInsight> deterministicInsights,
        AiThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(deterministic);
        ArgumentNullException.ThrowIfNull(deterministicInsights);
        ArgumentNullException.ThrowIfNull(thresholds);

        var culture = CultureInfo.CurrentCulture;
        var byId = photos.ToDictionary(p => p.Id);

        // Scene groups from embeddings.
        var window = thresholds.BurstWindowSeconds > 0 ? TimeSpan.FromSeconds(thresholds.BurstWindowSeconds) : (TimeSpan?)null;
        var sceneGroups = EmbeddingGrouper.Group(
            photos.Where(p => ai.TryGetValue(p.Id, out var a) && a.Embedding is not null)
                .Select(p => new EmbeddingGrouper.Item(p.Id, ai[p.Id].Embedding!, p.CaptureLocalTime, p.FilePath))
                .ToList(),
            thresholds.SceneSimilarityMin,
            window);
        var sceneOf = new Dictionary<Guid, (EmbeddingGrouper.SceneGroup Group, EmbeddingGrouper.Member Member)>();
        foreach (var g in sceneGroups)
        {
            foreach (var m in g.Members)
            {
                sceneOf[m.Id] = (g, m);
            }
        }

        // Faces counted with the current score threshold (all of them are shown in the badge count).
        IReadOnlyList<FaceFinding> FacesOf(Guid id) =>
            ai.TryGetValue(id, out var a) && a.Faces is { } f
                ? f.Faces.Where(x => x.Score >= thresholds.FaceMinScore).ToList()
                : [];

        // Main faces drive "soft" and the pick, so blurred background faces (spectators) do not.
        IReadOnlyList<FaceFinding> MainFacesOf(Guid id) => MainFaces(FacesOf(id));

        // Pick suggestion per group: scene group if the photo has one, else the deterministic similar-photo group.
        var groups = new Dictionary<string, List<Guid>>();
        foreach (var p in photos.Where(p => p.Status == ImportStatus.Imported))
        {
            var key = sceneOf.TryGetValue(p.Id, out var s) ? "scene:" + s.Group.Key
                : deterministicInsights.TryGetValue(p.Id, out var d) && d.GroupKey is { } k ? "similar:" + k
                : null;
            if (key is not null)
            {
                if (!groups.TryGetValue(key, out var list))
                {
                    groups[key] = list = [];
                }

                list.Add(p.Id);
            }
        }

        var picks = new Dictionary<Guid, (SuggestionConfidence Confidence, string Reason)>();
        foreach (var (key, members) in groups)
        {
            if (members.Count < 2 || Rank(members, MainFacesOf, deterministic) is not { } ranking)
            {
                continue;
            }

            picks[ranking.Best] = (ranking.Confidence, ranking.Reason + (key.StartsWith("scene:", StringComparison.Ordinal) ? " (scene group)" : " (similar-photo group)"));
        }

        var result = new Dictionary<Guid, AiInsight>();
        foreach (var photo in photos)
        {
            var hasAi = ai.TryGetValue(photo.Id, out var analysis);
            if (!hasAi && !picks.ContainsKey(photo.Id))
            {
                continue;
            }

            var lines = new List<InsightLine>();
            var faces = FacesOf(photo.Id);
            var main = MainFaces(faces);
            var soft = main.Count > 0 && main.All(f => f.Sharpness < thresholds.FaceSoftBelow);
            if (analysis?.Faces is not null)
            {
                lines.Add(new InsightLine(
                    "AI · Faces",
                    faces.Count == 0
                        ? "No faces found"
                        : main.Count == 0
                            ? string.Create(culture, $"{faces.Count} face(s), all too small to judge sharpness")
                            : string.Create(culture, $"{faces.Count} face(s), {main.Count} main; main faces sharpest {main.Max(f => f.Sharpness):N0}, softest {main.Min(f => f.Sharpness):N0}")
                                + (soft ? " · all main faces soft" : string.Empty),
                    string.Create(culture, $"YuNet face detector (score ≥ {thresholds.FaceMinScore:0.00}). Main faces are at least half the width of the largest face and at least {AiAnalysisService.MinFacePixelsForSharpness} px; their sharpness uses the same Laplacian measure as Analysis, and below {thresholds.FaceSoftBelow:N0} counts as soft. ") + Disclaimer,
                    soft));
            }

            string? sceneKey = null;
            var sceneSize = 0;
            if (sceneOf.TryGetValue(photo.Id, out var scene))
            {
                sceneKey = scene.Group.Key;
                sceneSize = scene.Group.Members.Count;
                var nearest = byId.TryGetValue(scene.Member.NearestId, out var n) ? n.FileName : "another photo";
                lines.Add(new InsightLine(
                    "AI · Similar scene",
                    string.Create(culture, $"Group of {sceneSize} · nearest: {nearest}, similarity {scene.Member.Similarity:0.000}"),
                    string.Create(culture, $"DINOv2 image embeddings, cosine similarity ≥ {thresholds.SceneSimilarityMin:0.00}")
                        + (window is null ? ", no time limit. " : string.Create(culture, $", within {thresholds.BurstWindowSeconds} s. ")) + Disclaimer,
                    false));
            }

            if (picks.TryGetValue(photo.Id, out var pick))
            {
                lines.Add(new InsightLine(
                    "AI · Suggested pick",
                    $"Suggested keep in its group · confidence {pick.Confidence}",
                    pick.Reason + ". " + Disclaimer,
                    false));
            }

            result[photo.Id] = new AiInsight
            {
                FaceCount = faces.Count,
                FacesSoft = soft,
                SceneGroupKey = sceneKey,
                SceneGroupSize = sceneSize,
                IsSuggestedPick = picks.ContainsKey(photo.Id),
                PickConfidence = picks.TryGetValue(photo.Id, out var p2) ? p2.Confidence : null,
                Details = lines,
            };
        }

        return result;
    }

    /// <summary>Share of the largest face's width a face needs to count as a main subject.</summary>
    public const double MainFaceRelativeWidth = 0.5;

    /// <summary>
    /// Faces large enough to be the subject: measurable sharpness and at least half the width of the largest face.
    /// </summary>
    internal static IReadOnlyList<FaceFinding> MainFaces(IReadOnlyList<FaceFinding> faces)
    {
        var measurable = faces.Where(f => f.Sharpness is not null).ToList();
        if (measurable.Count == 0)
        {
            return [];
        }

        var largest = measurable.Max(f => f.Width);
        return measurable.Where(f => f.Width >= largest * MainFaceRelativeWidth).ToList();
    }

    /// <summary>
    /// Ranks a group. If at least two members have main faces (see <see cref="MainFaces"/>), the key is each photo's softest main face (a pick should
    /// have every face sharp) and photos without faces are not candidates. Otherwise (no faces, or a single photo with a
    /// face, which may well be a false detection) the key is the whole-photo sharpness score.
    /// Confidence comes from the margin between the best and second-best keys.
    /// </summary>
    internal static (Guid Best, SuggestionConfidence Confidence, string Reason)? Rank(
        IReadOnlyList<Guid> members,
        Func<Guid, IReadOnlyList<FaceFinding>> facesOf,
        IReadOnlyDictionary<Guid, PhotoAnalysis> deterministic)
    {
        var useFaces = members.Count(id => facesOf(id).Count > 0) >= 2;
        var scored = members
            .Select(id =>
            {
                var faces = facesOf(id);
                double? key = useFaces
                    ? (faces.Count > 0 ? faces.Min(f => f.Sharpness) : null)
                    : deterministic.GetValueOrDefault(id)?.Sharpness?.Score;
                return (Id: id, Key: key);
            })
            .Where(x => x.Key is not null)
            .OrderByDescending(x => x.Key)
            .ThenBy(x => x.Id)
            .ToList();

        if (scored.Count < 2 || scored[0].Key <= 0)
        {
            return null;
        }

        var best = scored[0].Key!.Value;
        var margin = (best - scored[1].Key!.Value) / best;
        var confidence = margin >= 0.3 ? SuggestionConfidence.High : margin >= 0.1 ? SuggestionConfidence.Medium : SuggestionConfidence.Low;
        var basis = useFaces ? "its softest main face is the sharpest in the group" : "it has the highest sharpness score in the group";
        var reason = string.Create(CultureInfo.CurrentCulture, $"Suggested because {basis} ({best:N0} vs next {scored[1].Key:N0}, {margin:P0} ahead)");
        return (scored[0].Id, confidence, reason);
    }
}
