using System.Globalization;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// How the subject is found (ADR 0008, ADR 0010). Each main person's box is used as detected. Padding (a share of the
/// person's height) was tried for bats, raised arms and caps, but in the blind round 3 grading the unpadded masks were
/// better on 14 of 30 photos and the padded ones on none, so it is off; missing parts are left to click-to-select.
/// </summary>
public sealed record SubjectMaskOptions
{
    /// <summary>What the app uses: unpadded boxes, as in rounds 2 and 3.</summary>
    public static SubjectMaskOptions Default { get; } = new();

    /// <summary>Same as <see cref="Default"/>; named for the round 3 comparison.</summary>
    public static SubjectMaskOptions Unpadded => Default;

    /// <summary>The padding graded in round 3 (and rejected): 15 % on each side, 10 % above, 3 % below.</summary>
    public static SubjectMaskOptions Padded { get; } = new() { PadSide = 0.15, PadTop = 0.10, PadBottom = 0.03 };

    /// <summary>OpenCV Zoo's default confidence threshold for YOLOX.</summary>
    public float MinScore { get; init; } = 0.5f;

    /// <summary>A person is a main subject when at least this share of the tallest person's height.</summary>
    public double MainPersonRelativeHeight { get; init; } = 0.5;

    /// <summary>Added on the left and on the right, as a share of the person's height (a bat or an arm held out).</summary>
    public double PadSide { get; init; }

    /// <summary>Added above, as a share of the person's height (raised arms, a bat lifted, a cap).</summary>
    public double PadTop { get; init; }

    /// <summary>Added below, as a share of the person's height (feet and shoes).</summary>
    public double PadBottom { get; init; }

    /// <summary>Part of the mask source, so other settings never reuse a cached mask.</summary>
    public string Tag => string.Create(CultureInfo.InvariantCulture, $"s{MinScore:0.##}m{MainPersonRelativeHeight:0.##}p{PadSide:0.###}-{PadTop:0.###}-{PadBottom:0.###}");
}

/// <summary>
/// Select subject: YOLOX-s finds the people, the main ones (by height) are kept, each one's box prompts
/// MobileSAM, and the masks are combined (ADR 0008). Clicks refine it:
/// <list type="bullet">
/// <item>an Include click inside a person's box is added to that person's prompt; outside every box it selects a
/// region of its own (something the detector missed, such as a ball or a hidden player);</item>
/// <item>an Exclude click inside a main person's box removes that person (not this umpire), unless the same box also
/// has an Include click, in which case it refines that person's outline; outside every box it refines every prompt.</item>
/// </list>
/// Clicks inside overlapping boxes belong to the smallest box. Points are in the photo's upright, uncropped frame.
/// </summary>
public sealed class SubjectMaskGenerator(IAiRuntime runtime, SubjectMaskOptions? options = null) : IMaskGenerator
{
    /// <summary>Bump when the rules here change in a way that changes the mask.</summary>
    public const int RulesVersion = 1;

    private readonly SubjectMaskOptions _options = options ?? SubjectMaskOptions.Default;

    public MaskKind Kind => MaskKind.Subject;

    /// <summary>From the catalogue (not the loaded models), so the cache key is known before anything loads.</summary>
    public string Source => $"subject@{RulesVersion}-{_options.Tag}/{AiModelCatalog.PersonDetector.VersionTag}/{AiModelCatalog.SubjectMaskEncoder.VersionTag}/{AiModelCatalog.SubjectMaskDecoder.VersionTag}";

    /// <remarks>Loads the models on first use (well under a second), so call it off the UI thread.</remarks>
    public string? UnavailableReason =>
        runtime.GetPersonDetector() is null || runtime.GetPromptSegmenter() is null
            ? runtime.LastError ?? "Select subject needs the YOLOX-s person detector and both MobileSAM files. Install them in AI models."
            : null;

    public MaskGeneration Generate(AnalysisImage image, IReadOnlyList<MaskPoint> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(points);

        var detector = runtime.GetPersonDetector();
        var segmenter = runtime.GetPromptSegmenter();
        if (detector is null || segmenter is null)
        {
            return MaskGeneration.NotFound(UnavailableReason ?? "The subject mask models are not available.");
        }

        var people = detector.Detect(image, _options.MinScore, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var main = MainPeople(people.Select(p => p.Box).ToList(), _options.MainPersonRelativeHeight);
        var plan = Plan(main, points, image.Width, image.Height, _options);
        if (plan.Prompts.Count == 0)
        {
            return MaskGeneration.NotFound(main.Count == 0
                ? "No people found. Click the subject to select it."
                : "Every person was excluded. Click the subject to select it.");
        }

        var masks = segmenter.Segment(image, plan.Prompts, cancellationToken);
        var mask = Union(masks, image.Width, image.Height);
        if (mask.Coverage() <= 0)
        {
            return MaskGeneration.NotFound("The subject mask came out empty. Click the subject to select it.");
        }

        return MaskGeneration.Found(mask, Describe(plan.People, plan.ClickRegions, points.Count));
    }

    // ------------------------------------------------------------------ pure rules (tested without models)

    /// <summary>What will be segmented: one prompt per kept person (its box plus its clicks) and per lone click.</summary>
    internal sealed record PromptPlan(IReadOnlyList<SegmentPrompt> Prompts, int People, int ClickRegions);

    /// <summary>People at least <paramref name="relativeHeight"/> of the tallest one's height, left to right.</summary>
    internal static List<NormalisedBox> MainPeople(IReadOnlyList<NormalisedBox> people, double relativeHeight)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var tallest = people.Max(p => p.Height);
        return people.Where(p => p.Height >= tallest * relativeHeight).OrderBy(p => p.Left).ToList();
    }

    /// <summary>The box grown by the padding (a share of the person's height, in pixels on each axis), clamped to the image.</summary>
    internal static NormalisedBox Pad(NormalisedBox box, int width, int height, SubjectMaskOptions options)
    {
        var personHeightPx = box.Height * height;
        var side = options.PadSide * personHeightPx / width;
        return new NormalisedBox(
            Math.Max(0, box.Left - side),
            Math.Max(0, box.Top - (options.PadTop * box.Height)),
            Math.Min(1, box.Right + side),
            Math.Min(1, box.Bottom + (options.PadBottom * box.Height)));
    }

    internal static PromptPlan Plan(IReadOnlyList<NormalisedBox> mainPeople, IReadOnlyList<MaskPoint> points, int width, int height, SubjectMaskOptions options)
    {
        // Clicks belong to the smallest (unpadded) box they fall in, so a click on a player in a group picks that player.
        var owner = new int[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            owner[i] = -1;
            var best = double.MaxValue;
            for (var b = 0; b < mainPeople.Count; b++)
            {
                var area = mainPeople[b].Width * mainPeople[b].Height;
                if (mainPeople[b].Contains(points[i].X, points[i].Y) && area < best)
                {
                    best = area;
                    owner[i] = b;
                }
            }
        }

        var looseExcludes = points.Where((p, i) => !p.Include && owner[i] < 0).ToList();
        var prompts = new List<SegmentPrompt>();
        var people = 0;
        for (var b = 0; b < mainPeople.Count; b++)
        {
            var own = points.Where((_, i) => owner[i] == b).ToList();
            var included = own.Any(p => p.Include);
            if (!included && own.Count > 0)
            {
                continue; // only Exclude clicks on this person: not part of the subject
            }

            prompts.Add(new SegmentPrompt(Pad(mainPeople[b], width, height, options), [.. own, .. looseExcludes]));
            people++;
        }

        var lone = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].Include && owner[i] < 0)
            {
                prompts.Add(new SegmentPrompt(null, [points[i], .. looseExcludes]));
                lone++;
            }
        }

        return new PromptPlan(prompts, people, lone);
    }

    internal static MaskImage Union(IReadOnlyList<MaskImage> masks, int width, int height)
    {
        var alpha = new byte[width * height];
        foreach (var mask in masks)
        {
            if (mask.Width != width || mask.Height != height)
            {
                throw new ArgumentException("Every mask must match the image's size.", nameof(masks));
            }

            for (var i = 0; i < alpha.Length; i++)
            {
                alpha[i] = Math.Max(alpha[i], mask.Alpha[i]);
            }
        }

        return new MaskImage(width, height, alpha);
    }

    internal static string Describe(int people, int clickRegions, int clicks)
    {
        var parts = new List<string>();
        if (people > 0)
        {
            parts.Add(people == 1 ? "1 person" : $"{people} people");
        }

        if (clickRegions > 0)
        {
            parts.Add(clickRegions == 1 ? "1 clicked area" : $"{clickRegions} clicked areas");
        }

        var what = parts.Count == 0 ? "Nothing" : string.Join(" and ", parts);
        return clicks switch { 0 => what, 1 => what + " (1 click)", _ => $"{what} ({clicks} clicks)" };
    }
}
