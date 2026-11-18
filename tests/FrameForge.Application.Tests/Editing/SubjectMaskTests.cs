using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Tests.Editing;

/// <summary>Select subject's rules (main people, padding, clicks), run without the models.</summary>
public sealed class SubjectMaskTests
{
    private static readonly SubjectMaskOptions Options = SubjectMaskOptions.Padded;
    private static readonly NormalisedBox Batter = new(0.40, 0.20, 0.50, 0.90);
    private static readonly NormalisedBox Keeper = new(0.60, 0.40, 0.70, 0.85);
    private static readonly NormalisedBox Fielder = new(0.05, 0.40, 0.08, 0.55);

    [Fact]
    public void MainPeople_KeepsThoseAtLeastHalfTheTallest_LeftToRight()
    {
        var main = SubjectMaskGenerator.MainPeople([Keeper, Fielder, Batter], 0.5);

        Assert.Equal([Batter, Keeper], main);
        Assert.Empty(SubjectMaskGenerator.MainPeople([], 0.5));
    }

    [Fact]
    public void Pad_UsesThePersonsHeightInPixels_OnBothAxes_AndClamps()
    {
        // 1500 × 1000 photo, person 0.5 high = 500 px: 15 % = 75 px on each side = 0.05 of the width.
        var padded = SubjectMaskGenerator.Pad(new NormalisedBox(0.4, 0.3, 0.5, 0.8), 1500, 1000, Options);

        Assert.Equal(0.35, padded.Left, 9);
        Assert.Equal(0.55, padded.Right, 9);
        Assert.Equal(0.25, padded.Top, 9);   // 10 % of the height above
        Assert.Equal(0.815, padded.Bottom, 9); // 3 % below

        var edge = SubjectMaskGenerator.Pad(new NormalisedBox(0.0, 0.0, 0.1, 1.0), 1500, 1000, Options);
        Assert.Equal(new NormalisedBox(0, 0, edge.Right, 1), edge);
    }

    [Fact]
    public void TheAppDefault_PromptsWithTheBoxAsDetected()
    {
        // Round 3 (ADR 0010): unpadded boxes were graded better on 14 of 30 photos, padded ones on none.
        var plan = SubjectMaskGenerator.Plan([Batter], [], 1500, 1000, SubjectMaskOptions.Default);

        Assert.Equal(Batter, Assert.Single(plan.Prompts).Box);
        Assert.Equal(SubjectMaskOptions.Default, SubjectMaskOptions.Unpadded);
    }

    [Fact]
    public void Unpadded_LeavesTheBoxAsDetected() =>
        Assert.Equal(Batter, SubjectMaskGenerator.Pad(Batter, 1500, 1000, SubjectMaskOptions.Unpadded));

    [Fact]
    public void Plan_WithoutClicks_PromptsEachMainPersonWithItsPaddedBox()
    {
        var plan = SubjectMaskGenerator.Plan([Batter, Keeper], [], 1500, 1000, Options);

        Assert.Equal(2, plan.People);
        Assert.Equal(0, plan.ClickRegions);
        Assert.All(plan.Prompts, p => Assert.Empty(p.Points));
        Assert.Equal(SubjectMaskGenerator.Pad(Batter, 1500, 1000, Options), plan.Prompts[0].Box);
    }

    [Fact]
    public void ExcludeClickOnAPerson_RemovesThatPerson()
    {
        var notTheKeeper = new MaskPoint(0.65, 0.6, Include: false);

        var plan = SubjectMaskGenerator.Plan([Batter, Keeper], [notTheKeeper], 1500, 1000, Options);

        Assert.Equal(1, plan.People);
        Assert.Equal(SubjectMaskGenerator.Pad(Batter, 1500, 1000, Options), Assert.Single(plan.Prompts).Box);
    }

    [Fact]
    public void ExcludeClick_WithAnIncludeOnTheSamePerson_RefinesTheOutline()
    {
        MaskPoint[] clicks = [new(0.45, 0.5, Include: true), new(0.48, 0.88, Include: false)];

        var plan = SubjectMaskGenerator.Plan([Batter], clicks, 1500, 1000, Options);

        Assert.Equal(clicks, Assert.Single(plan.Prompts).Points);
    }

    [Fact]
    public void IncludeClickOutsideEveryBox_SelectsARegionOfItsOwn()
    {
        var ball = new MaskPoint(0.9, 0.1, Include: true);

        var plan = SubjectMaskGenerator.Plan([Batter], [ball], 1500, 1000, Options);

        Assert.Equal(1, plan.People);
        Assert.Equal(1, plan.ClickRegions);
        Assert.Null(plan.Prompts[1].Box);
        Assert.Equal([ball], plan.Prompts[1].Points);
    }

    [Fact]
    public void ExcludeClickOutsideEveryBox_RefinesEveryPrompt()
    {
        var shadow = new MaskPoint(0.2, 0.95, Include: false);

        var plan = SubjectMaskGenerator.Plan([Batter, Keeper], [shadow], 1500, 1000, Options);

        Assert.Equal(2, plan.People);
        Assert.All(plan.Prompts, p => Assert.Equal([shadow], p.Points));
    }

    [Fact]
    public void ClickInOverlappingBoxes_BelongsToTheSmallest()
    {
        var group = new NormalisedBox(0.3, 0.1, 0.8, 0.95);
        var notThisOne = new MaskPoint(0.65, 0.6, Include: false); // inside both the group box and the keeper's

        var plan = SubjectMaskGenerator.Plan([group, Keeper], [notThisOne], 1500, 1000, Options);

        Assert.Equal(SubjectMaskGenerator.Pad(group, 1500, 1000, Options), Assert.Single(plan.Prompts).Box);
    }

    [Fact]
    public void Union_IsThePixelwiseMaximum()
    {
        var a = new MaskImage(2, 1, [255, 0]);
        var b = new MaskImage(2, 1, [0, 128]);

        Assert.Equal([255, 128], SubjectMaskGenerator.Union([a, b], 2, 1).Alpha);
        Assert.Throws<ArgumentException>(() => SubjectMaskGenerator.Union([new MaskImage(1, 1, [0])], 2, 1));
    }

    [Theory]
    [InlineData(2, 0, 0, "2 people")]
    [InlineData(1, 1, 2, "1 person and 1 clicked area (2 clicks)")]
    [InlineData(0, 2, 2, "2 clicked areas (2 clicks)")]
    public void Describe_SaysWhatWasSelected(int people, int regions, int clicks, string expected) =>
        Assert.Equal(expected, SubjectMaskGenerator.Describe(people, regions, clicks));

    [Fact]
    public void Source_NamesTheRulesTheSettingsAndEveryModelFile()
    {
        var source = new SubjectMaskGenerator(new Runtime()).Source;

        Assert.Contains(AiModelCatalog.PersonDetector.VersionTag, source, StringComparison.Ordinal);
        Assert.Contains(AiModelCatalog.SubjectMaskEncoder.VersionTag, source, StringComparison.Ordinal);
        Assert.Contains(AiModelCatalog.SubjectMaskDecoder.VersionTag, source, StringComparison.Ordinal);
        Assert.NotEqual(source, new SubjectMaskGenerator(new Runtime(), SubjectMaskOptions.Padded).Source);
        Assert.True(source.Length <= MaskReference.MaxSourceLength);
    }

    // ------------------------------------------------------------------ with fake models

    private static readonly AnalysisImage Image = AnalysisImage.Create(30, 20, (_, _) => (90, 120, 60));

    [Fact]
    public void ModelsNotInstalled_IsUnavailable_WithTheReason()
    {
        var generator = new SubjectMaskGenerator(new Runtime { Installed = false });

        Assert.Contains("Install them in AI models", generator.UnavailableReason, StringComparison.Ordinal);
        Assert.False(generator.Generate(Image, [], TestContext.Current.CancellationToken).HasMask);
    }

    [Fact]
    public void NoPeople_AndNoClicks_SaysSo()
    {
        var result = new SubjectMaskGenerator(new Runtime()).Generate(Image, [], TestContext.Current.CancellationToken);

        Assert.False(result.HasMask);
        Assert.Equal("No people found. Click the subject to select it.", result.Summary);
    }

    [Fact]
    public void People_AreSegmented_AndCombined()
    {
        var runtime = new Runtime { People = [new DetectedPerson(Batter, 0.9f), new DetectedPerson(Keeper, 0.8f), new DetectedPerson(Fielder, 0.7f)] };

        var result = new SubjectMaskGenerator(runtime).Generate(Image, [], TestContext.Current.CancellationToken);

        Assert.True(result.HasMask);
        Assert.Equal("2 people", result.Summary);
        Assert.Equal(2, runtime.Segmenter.LastPrompts!.Count); // the distant fielder is not a main person
        Assert.Equal(0.5f, runtime.Detector.LastMinScore);
        Assert.Equal((Image.Width, Image.Height), (result.Mask!.Width, result.Mask.Height));
    }

    [Fact]
    public void RefiningTheSameImage_DoesNotRunTheDetectorAgain()
    {
        var runtime = new Runtime { People = [new DetectedPerson(Batter, 0.9f)] };
        var generator = new SubjectMaskGenerator(runtime);

        generator.Generate(Image, [], TestContext.Current.CancellationToken);
        generator.Generate(Image, [new MaskPoint(0.45, 0.5, true)], TestContext.Current.CancellationToken);
        generator.Generate(AnalysisImage.Create(30, 20, (_, _) => (1, 2, 3)), [], TestContext.Current.CancellationToken);

        Assert.Equal(2, runtime.Detector.Calls); // once per image, not once per click
    }

    private sealed class Runtime : IAiRuntime
    {
        public bool Installed { get; init; } = true;

        public IReadOnlyList<DetectedPerson> People { get; init; } = [];

        public Detector Detector { get; } = new();

        public Segmenter Segmenter { get; } = new();

        public string? LastError => null;

        public IFaceDetector? GetFaceDetector() => null;

        public IImageEmbedder? GetImageEmbedder() => null;

        public IPersonDetector? GetPersonDetector()
        {
            Detector.People = People;
            return Installed ? Detector : null;
        }

        public IPromptSegmenter? GetPromptSegmenter() => Installed ? Segmenter : null;

        public void Reset()
        {
        }
    }

    private sealed class Detector : IPersonDetector
    {
        public IReadOnlyList<DetectedPerson> People { get; set; } = [];

        public float? LastMinScore { get; private set; }

        public int Calls { get; private set; }

        public string VersionTag => "fake-people";

        public IReadOnlyList<DetectedPerson> Detect(AnalysisImage image, float minScore, CancellationToken cancellationToken)
        {
            LastMinScore = minScore;
            Calls++;
            return People;
        }
    }

    /// <summary>Fills each prompt's box (or the clicked pixel), so the union can be checked.</summary>
    private sealed class Segmenter : IPromptSegmenter
    {
        public IReadOnlyList<SegmentPrompt>? LastPrompts { get; private set; }

        public string VersionTag => "fake-segmenter";

        public IReadOnlyList<MaskImage> Segment(AnalysisImage image, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken)
        {
            LastPrompts = prompts;
            return prompts.Select(p =>
            {
                var alpha = new byte[image.Width * image.Height];
                var box = p.Box ?? new NormalisedBox(p.Points[0].X, p.Points[0].Y, p.Points[0].X, p.Points[0].Y);
                for (var y = (int)(box.Top * image.Height); y < Math.Max((int)(box.Top * image.Height) + 1, (int)(box.Bottom * image.Height)); y++)
                {
                    for (var x = (int)(box.Left * image.Width); x < Math.Max((int)(box.Left * image.Width) + 1, (int)(box.Right * image.Width)); x++)
                    {
                        alpha[(Math.Min(y, image.Height - 1) * image.Width) + Math.Min(x, image.Width - 1)] = 255;
                    }
                }

                return new MaskImage(image.Width, image.Height, alpha);
            }).ToList();
        }
    }
}
