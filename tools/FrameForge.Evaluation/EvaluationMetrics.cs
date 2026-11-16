using System.Globalization;

namespace FrameForge.Evaluation;

/// <summary>Human label for one photo. <c>Group</c> empty = not part of a set of similar shots.</summary>
internal sealed record PhotoLabel(string File, string Group, LabelDecision Decision, int? Faces);

internal enum LabelDecision
{
    Other,
    Keep,
    Reject,
}

internal sealed record PrecisionRecall(int TruePositives, int Predicted, int Actual)
{
    public double? Precision => Predicted == 0 ? null : (double)TruePositives / Predicted;

    public double? Recall => Actual == 0 ? null : (double)TruePositives / Actual;
}

/// <summary>Pure metric functions, unit-tested. Undefined metrics (empty denominators) are null, never 0 or 1.</summary>
internal static class EvaluationMetrics
{
    /// <summary>
    /// Labels CSV: <c>file,group,decision,faces</c> with a header row. decision is keep, reject or blank; faces is a
    /// whole number or blank (not labelled). Lines starting with # are comments.
    /// </summary>
    public static List<PhotoLabel> ParseLabels(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var labels = new List<PhotoLabel>();
        var lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || (lineNo == 1 && line.StartsWith("file", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var cells = line.Split(',').Select(c => c.Trim()).ToArray();
            if (cells.Length < 1 || cells[0].Length == 0)
            {
                throw new FormatException($"Line {lineNo}: missing file name.");
            }

            var decision = Cell(cells, 2).ToUpperInvariant() switch
            {
                "KEEP" => LabelDecision.Keep,
                "REJECT" => LabelDecision.Reject,
                "" => LabelDecision.Other,
                var other => throw new FormatException($"Line {lineNo}: decision '{other}' is not keep, reject or blank."),
            };
            int? faces = Cell(cells, 3) is { Length: > 0 } f
                ? int.TryParse(f, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException($"Line {lineNo}: faces '{f}' is not a whole number.")
                : null;
            labels.Add(new PhotoLabel(cells[0], Cell(cells, 1), decision, faces));
        }

        return labels;
    }

    /// <summary>Pairwise grouping: a pair counts when both photos are in the same group.</summary>
    public static PrecisionRecall Pairwise(IReadOnlyDictionary<string, string> predictedGroup, IReadOnlyDictionary<string, string> labelledGroup)
    {
        ArgumentNullException.ThrowIfNull(predictedGroup);
        ArgumentNullException.ThrowIfNull(labelledGroup);
        var predicted = Pairs(predictedGroup);
        var actual = Pairs(labelledGroup);
        return new PrecisionRecall(predicted.Intersect(actual).Count(), predicted.Count, actual.Count);
    }

    /// <summary>Face recall approximated by counts: Σ min(detected, labelled) / Σ labelled, over photos with a face label.</summary>
    public static (double? Recall, int Extra) FaceCounts(IEnumerable<(int Detected, int Labelled)> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        int matched = 0, labelled = 0, extra = 0;
        foreach (var (d, l) in photos)
        {
            matched += Math.Min(d, l);
            labelled += l;
            extra += Math.Max(0, d - l);
        }

        return (labelled == 0 ? null : (double)matched / labelled, extra);
    }

    /// <summary>
    /// Pick agreement: share of AI picks labelled keep, among picks labelled keep or reject. False rejection: share of human keeps (inside an AI
    /// group) that were not suggested — the photos a user following the suggestions blindly would lose.
    /// </summary>
    public static (double? Agreement, double? FalseRejection) Picks(IReadOnlyCollection<string> aiPicks, IReadOnlyCollection<string> inAiGroups, IReadOnlyDictionary<string, LabelDecision> labels)
    {
        ArgumentNullException.ThrowIfNull(aiPicks);
        ArgumentNullException.ThrowIfNull(inAiGroups);
        ArgumentNullException.ThrowIfNull(labels);
        var labelledPicks = aiPicks.Where(p => labels.GetValueOrDefault(p) is LabelDecision.Keep or LabelDecision.Reject).ToList();
        var agreement = labelledPicks.Count == 0 ? (double?)null : (double)labelledPicks.Count(p => labels[p] == LabelDecision.Keep) / labelledPicks.Count;
        var keepers = inAiGroups.Where(f => labels.GetValueOrDefault(f) == LabelDecision.Keep).ToList();
        var falseRejection = keepers.Count == 0 ? (double?)null : (double)keepers.Count(k => !aiPicks.Contains(k)) / keepers.Count;
        return (agreement, falseRejection);
    }

    public static string Percent(double? value) => value is { } v ? v.ToString("P1", CultureInfo.InvariantCulture) : "n/a";

    private static string Cell(string[] cells, int index) => index < cells.Length ? cells[index] : string.Empty;

    private static HashSet<(string, string)> Pairs(IReadOnlyDictionary<string, string> groupOf)
    {
        var pairs = new HashSet<(string, string)>();
        foreach (var group in groupOf.Where(kv => kv.Value.Length > 0).GroupBy(kv => kv.Value, kv => kv.Key))
        {
            var members = group.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray();
            for (var i = 0; i < members.Length; i++)
            {
                for (var j = i + 1; j < members.Length; j++)
                {
                    pairs.Add((members[i].ToUpperInvariant(), members[j].ToUpperInvariant()));
                }
            }
        }

        return pairs;
    }
}
