using System.Globalization;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;

namespace FrameForge.Evaluation.Sheets;

/// <summary>What a light sheet stores per pair: which side shows Auto light, and what Auto changed (for the report).</summary>
internal sealed record LightPair(string Id, string File, bool AutoOnLeft, string Variant, string Change);

/// <summary>What a crop sheet stores per photo: the upright full-resolution size and the detected faces.</summary>
internal sealed record CropItem(string Id, string File, int SourceWidth, int SourceHeight, IReadOnlyList<FaceFinding> Faces);

/// <summary>A user's crop in normalised coordinates of the upright photo, or null for "keep the whole frame".</summary>
internal sealed record UserCrop(double X, double Y, double Width, double Height);

internal enum LightChoice
{
    Left,
    Right,
    Same,
}

/// <summary>Pure scoring for the judging sheets: answer parsing, A/B tallies with intervals, and crop agreement.</summary>
internal static class SheetScoring
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------------ answers

    /// <summary>Parses "id,answer..." lines (header and blank lines allowed). Later lines win, so re-answering is fine.</summary>
    public static Dictionary<string, string[]> ParseAnswers(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var raw in lines)
        {
            n++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || (n == 1 && line.StartsWith("id,", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var parts = line.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 2 || parts[0].Length == 0)
            {
                throw new FormatException(string.Create(Culture, $"Line {n}: expected \"id,answer\", got \"{line}\"."));
            }

            result[parts[0]] = parts[1..];
        }

        return result;
    }

    public static LightChoice ParseLightChoice(string id, string value) => value.ToLowerInvariant() switch
    {
        "left" => LightChoice.Left,
        "right" => LightChoice.Right,
        "same" => LightChoice.Same,
        _ => throw new FormatException(string.Create(Culture, $"The answer for {id} must be left, right or same, got \"{value}\".")),
    };

    /// <summary>"none" means keep the whole frame; otherwise x,y,w,h in 0–1.</summary>
    public static UserCrop? ParseCrop(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 1 && values[0].Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (values.Length != 4 || !values.All(v => double.TryParse(v, NumberStyles.Float, Culture, out _)))
        {
            throw new FormatException("A crop answer is \"none\" or four numbers: x,y,width,height.");
        }

        var v = values.Select(s => double.Parse(s, Culture)).ToArray();
        if (v[2] <= 0 || v[3] <= 0 || v[0] < -1e-6 || v[1] < -1e-6 || v[0] + v[2] > 1 + 1e-6 || v[1] + v[3] > 1 + 1e-6)
        {
            throw new FormatException("A crop must lie inside the photo (0–1) and have a positive size.");
        }

        return new UserCrop(v[0], v[1], v[2], v[3]);
    }

    // ------------------------------------------------------------------ light A/B

    internal sealed record LightTally(int Auto, int Original, int Same, int Unanswered)
    {
        public int Decided => Auto + Original;

        /// <summary>Share of decided pairs where Auto was preferred, or null when none were decided.</summary>
        public double? AutoShare => Decided == 0 ? null : (double)Auto / Decided;
    }

    public static LightTally TallyLight(IReadOnlyList<LightPair> pairs, IReadOnlyDictionary<string, LightChoice> answers)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(answers);
        int auto = 0, original = 0, same = 0, missing = 0;
        foreach (var pair in pairs)
        {
            if (!answers.TryGetValue(pair.Id, out var choice))
            {
                missing++;
            }
            else if (choice == LightChoice.Same)
            {
                same++;
            }
            else if ((choice == LightChoice.Left) == pair.AutoOnLeft)
            {
                auto++;
            }
            else
            {
                original++;
            }
        }

        return new LightTally(auto, original, same, missing);
    }

    /// <summary>Wilson score interval (95 %) for a proportion; honest at small n, unlike the normal approximation.</summary>
    public static (double Low, double High) Wilson(int successes, int n, double z = 1.96)
    {
        if (n == 0)
        {
            return (0, 1);
        }

        var p = (double)successes / n;
        var denominator = 1 + (z * z / n);
        var centre = (p + (z * z / (2 * n))) / denominator;
        var half = z * Math.Sqrt((p * (1 - p) / n) + (z * z / (4.0 * n * n))) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>Exact two-sided sign test: how likely a split at least this uneven is if there were no real preference.</summary>
    public static double SignTestP(int a, int b)
    {
        var n = a + b;
        if (n == 0)
        {
            return 1;
        }

        var k = Math.Min(a, b);
        double tail = 0, term = Math.Pow(0.5, n); // C(n,0) · 0.5^n
        for (var i = 0; i <= k; i++)
        {
            tail += term;
            term = term * (n - i) / (i + 1);
        }

        return Math.Min(1, 2 * tail);
    }

    // ------------------------------------------------------------------ crop agreement

    /// <summary>Intersection over union of two rectangles in the same normalised space (1 = identical, 0 = disjoint).</summary>
    public static double Iou(CropRect a, CropRect b)
    {
        var w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        var intersection = Math.Max(0, w) * Math.Max(0, h);
        var union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    internal sealed record CropComparison(string Id, string File, bool UserKeptFrame, string? BestOption, double? BestIou, double? MediumIou, bool UserBelowMinimum, string? Unavailable)
    {
        /// <summary>Your crop's area ÷ the closest option's area (above 1 = you framed looser).</summary>
        public double? AreaRatio { get; init; }

        /// <summary>Your crop's centre minus the closest option's, in frame widths/heights (positive = right/lower).</summary>
        public (double X, double Y)? CentreShift { get; init; }
    }

    /// <summary>
    /// Compares the user's crop with Auto crop's options computed at the user's own aspect ratio, so the score measures
    /// framing (where and how tight), not the choice of ratio.
    /// </summary>
    public static CropComparison CompareCrop(CropItem item, UserCrop? user, double minFaceScore, int minLongSide = AutoCrop.DefaultMinLongSide)
    {
        ArgumentNullException.ThrowIfNull(item);
        var whole = new CropRect(0, 0, 1, 1);
        var target = user is null ? whole : new CropRect(user.X, user.Y, user.Width, user.Height);
        var ratio = target.Width * item.SourceWidth / (target.Height * item.SourceHeight);
        var suggestions = AutoCrop.Suggest(item.SourceWidth, item.SourceHeight, item.Faces, EditRecipe.Neutral, ratio, minFaceScore, minLongSide);
        var belowMinimum = user is not null && Math.Max(target.Width * item.SourceWidth, target.Height * item.SourceHeight) < minLongSide - 1;
        if (!suggestions.HasOptions)
        {
            return new CropComparison(item.Id, item.File, user is null, null, null, null, belowMinimum, suggestions.Unavailable);
        }

        // With a neutral recipe the crop is already in upright-photo coordinates.
        var scored = suggestions.Options.Select(o => (o.Name, Iou: Iou(o.Recipe.Crop, target))).ToList();
        var best = scored.MaxBy(s => s.Iou);
        double? medium = scored.Any(s => s.Name == "Medium") ? scored.First(s => s.Name == "Medium").Iou : null;
        var closest = suggestions.Options.First(o => o.Name == best.Name).Recipe.Crop;
        return new CropComparison(item.Id, item.File, user is null, best.Name, best.Iou, medium, belowMinimum, null)
        {
            AreaRatio = target.Width * target.Height / (closest.Width * closest.Height),
            CentreShift = ((target.X + (target.Width / 2)) - (closest.X + (closest.Width / 2)), (target.Y + (target.Height / 2)) - (closest.Y + (closest.Height / 2))),
        };
    }

    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }
}
