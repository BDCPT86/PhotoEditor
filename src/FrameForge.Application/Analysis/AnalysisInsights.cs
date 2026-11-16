using System.Globalization;
using FrameForge.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Analysis;

/// <summary>
/// How stored measurements are interpreted. Changing these never requires re-analysis: insights are rebuilt
/// from cached values in memory.
/// </summary>
public sealed record AnalysisThresholds
{
    public static AnalysisThresholds Default { get; } = new();

    public double BlurScore { get; init; } = 60;

    public double HighlightClipPercent { get; init; } = 1.0;

    public double ShadowClipPercent { get; init; } = 5.0;

    public int MaxHashDistance { get; init; } = 10;

    /// <summary>0 = similar photos are grouped regardless of capture time.</summary>
    public int BurstWindowSeconds { get; init; } = 10;
}

/// <summary>One explained finding: what was measured, the value, and how it was judged.</summary>
public sealed record InsightLine(string Topic, string Value, string Explanation, bool IsFlagged);

/// <summary>Everything the UI shows about one photo's analysis, derived from measurements plus thresholds.</summary>
public sealed record PhotoInsight
{
    public static PhotoInsight NotAnalysed { get; } = new();

    public bool IsAnalysed { get; init; }

    public bool LikelyBlurry { get; init; }

    public bool HighlightsClipped { get; init; }

    public bool ShadowsClipped { get; init; }

    /// <summary>Near-duplicate group key, or null when the photo is not in a group.</summary>
    public string? GroupKey { get; init; }

    public int GroupSize { get; init; }

    /// <summary>Position of the group among all groups (0-based), for grouped ordering.</summary>
    public int GroupOrder { get; init; } = -1;

    public bool IsSharpestInGroup { get; init; }

    /// <summary>Other photos with byte-identical content.</summary>
    public int ExactDuplicateCount { get; init; }

    public IReadOnlyList<InsightLine> Details { get; init; } = [];
}

/// <summary>
/// Builds explainable insights from cached measurements. Pure and deterministic: same photos, measurements and
/// thresholds always give the same insights and groups.
/// </summary>
public static class AnalysisInsights
{
    public const string HeuristicNote = "Measurements are heuristics, not AI; only lines marked “AI ·” come from the optional models. Either can be wrong; nothing is changed or deleted automatically.";

    public static IReadOnlyDictionary<Guid, PhotoInsight> Build(
        IReadOnlyList<Photo> photos,
        IReadOnlyDictionary<Guid, PhotoAnalysis> analyses,
        AnalysisThresholds thresholds,
        AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(analyses);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(settings);

        var byId = photos.ToDictionary(p => p.Id);
        var culture = CultureInfo.CurrentCulture;

        // Near-duplicate groups from perceptual hashes.
        var hashed = photos
            .Where(p => analyses.TryGetValue(p.Id, out var a) && a.PerceptualHash is not null)
            .Select(p => new SimilarityItem(p.Id, analyses[p.Id].PerceptualHash!.Value, p.CaptureLocalTime, p.FilePath))
            .ToList();
        var window = thresholds.BurstWindowSeconds > 0 ? TimeSpan.FromSeconds(thresholds.BurstWindowSeconds) : (TimeSpan?)null;
        var groups = SimilarityGrouper.Group(hashed, thresholds.MaxHashDistance, window);

        var membership = new Dictionary<Guid, (SimilarityGroup Group, SimilarityMember Member, int Order)>();
        for (var g = 0; g < groups.Count; g++)
        {
            foreach (var member in groups[g].Members)
            {
                membership[member.Id] = (groups[g], member, g);
            }
        }

        var sharpest = groups.ToDictionary(
            g => g.Key,
            g => g.Members
                .Select(m => (m.Id, Score: analyses[m.Id].Sharpness?.Score ?? double.MinValue))
                .OrderByDescending(x => x.Score)
                .First().Id);

        // Exact duplicates from content hashes.
        var exact = SimilarityGrouper.GroupExact(
            photos.Where(p => analyses.TryGetValue(p.Id, out var a) && a.ContentHash is not null)
                .Select(p => (p.Id, analyses[p.Id].ContentHash!, p.FilePath)));
        var exactPeers = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var set in exact)
        {
            foreach (var id in set)
            {
                exactPeers[id] = set.Where(other => other != id).ToList();
            }
        }

        var insights = new Dictionary<Guid, PhotoInsight>(photos.Count);
        foreach (var photo in photos)
        {
            if (!analyses.TryGetValue(photo.Id, out var analysis))
            {
                continue;
            }

            var lines = new List<InsightLine>();
            var blurry = false;
            var highlights = false;
            var shadows = false;

            if (analysis.Sharpness is { } s)
            {
                blurry = s.Score < thresholds.BlurScore;
                lines.Add(new InsightLine(
                    "Sharpness",
                    string.Create(culture, $"{s.Score:N0} (sharpest region: row {s.PeakRow + 1}, column {s.PeakColumn + 1} of {s.GridSize}×{s.GridSize}; whole image {s.GlobalVariance:N0})"),
                    string.Create(culture, $"Variance of the Laplacian (fine-detail contrast) at {s.AnalysedWidth}×{s.AnalysedHeight} px. ")
                        + (blurry
                            ? string.Create(culture, $"Below your threshold of {thresholds.BlurScore:N0}, so marked likely blurry.")
                            : string.Create(culture, $"At or above your threshold of {thresholds.BlurScore:N0}."))
                        + " Noise raises this score; flat, low-contrast scenes lower it.",
                    blurry));
            }

            if (analysis.Exposure is { } e)
            {
                highlights = e.HighlightClippedPercent > thresholds.HighlightClipPercent;
                shadows = e.ShadowClippedPercent > thresholds.ShadowClipPercent;
                lines.Add(new InsightLine(
                    "Exposure",
                    string.Create(culture, $"Mean brightness {e.MeanLuminance:N0} / 255 · highlights clipped {e.HighlightClippedPercent:0.##}% · shadows clipped {e.ShadowClippedPercent:0.##}%"),
                    string.Create(culture, $"Share of pixels with a channel at 254–255 (limit {thresholds.HighlightClipPercent:0.##}%) and with all channels at 0–1 (limit {thresholds.ShadowClipPercent:0.##}%), measured on the JPEG as saved."),
                    highlights || shadows));
            }

            string? groupKey = null;
            int groupSize = 0, groupOrder = -1;
            var isSharpest = false;
            if (membership.TryGetValue(photo.Id, out var m))
            {
                groupKey = m.Group.Key;
                groupSize = m.Group.Members.Count;
                groupOrder = m.Order;
                isSharpest = sharpest[m.Group.Key] == photo.Id && analysis.Sharpness is not null;
                var nearest = byId.TryGetValue(m.Member.NearestId, out var n) ? n.FileName : "another photo";
                var timing = m.Member.SecondsFromNearest is { } secs
                    ? string.Create(culture, $", taken {secs:0.#} s apart")
                    : string.Empty;
                var windowText = window is null ? "no time limit" : string.Create(culture, $"within {thresholds.BurstWindowSeconds} s");
                lines.Add(new InsightLine(
                    "Similar photos",
                    string.Create(culture, $"Group of {groupSize} · nearest: {nearest}, {m.Member.NearestDistance} of 64 bits differ{timing}")
                        + (isSharpest ? " · sharpest in group" : string.Empty),
                    string.Create(culture, $"Perceptual hash comparison: at most {thresholds.MaxHashDistance} bits may differ, {windowText}. ")
                        + "Linking is transitive, so members can be further apart from each other than from their nearest.",
                    false));
            }

            var peers = exactPeers.GetValueOrDefault(photo.Id) ?? [];
            if (peers.Count > 0)
            {
                lines.Add(new InsightLine(
                    "Exact duplicate",
                    "Same file content as " + string.Join(", ", peers.Take(3).Select(id => byId.TryGetValue(id, out var p) ? p.FilePath : id.ToString()))
                        + (peers.Count > 3 ? string.Create(culture, $" and {peers.Count - 3} more") : string.Empty),
                    "SHA-256 of the file bytes is identical.",
                    true));
            }

            if (analysis.PerceptualHash is { } ph)
            {
                lines.Add(new InsightLine("Perceptual hash", PerceptualHasher.Format(ph), $"Algorithm {PerceptualHasher.Version}.", false));
            }

            insights[photo.Id] = new PhotoInsight
            {
                IsAnalysed = true,
                LikelyBlurry = blurry,
                HighlightsClipped = highlights,
                ShadowsClipped = shadows,
                GroupKey = groupKey,
                GroupSize = groupSize,
                GroupOrder = groupOrder,
                IsSharpestInGroup = isSharpest,
                ExactDuplicateCount = peers.Count,
                Details = lines,
            };
        }

        return insights;
    }
}
