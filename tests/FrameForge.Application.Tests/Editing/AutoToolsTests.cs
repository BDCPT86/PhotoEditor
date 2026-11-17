using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Tests.Editing;

public sealed class AutoToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ Auto light

    [Fact]
    public void AutoTone_DarkPhoto_IsBrightened_ThreeNamedOptions()
    {
        var dark = AnalysisImage.Create(200, 150, (x, y) => ((byte)(80 + (x % 10)), (byte)(85 + (y % 10)), 80));

        var result = AutoTone.Suggest(dark, EditRecipe.Neutral);

        Assert.Equal(["Natural", "Brighter", "Punchy"], result.Options.Select(o => o.Name));
        Assert.All(result.Options, o => Assert.True(o.Recipe.Exposure > 0.4, $"{o.Name}: {o.Recipe.Exposure}"));
        Assert.True(result.Named("Brighter")!.Recipe.Exposure > result.Named("Natural")!.Recipe.Exposure);
        Assert.Equal(25, result.Named("Punchy")!.Recipe.Contrast);
        Assert.All(result.Options, o => Assert.Empty(o.Recipe.Validate()));
        Assert.Contains("EV", result.Options[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoTone_BrightButIntactScene_IsLeftAsShot()
    {
        // Sunny grass and white wall, nothing clipped: correctly exposed, so Natural must not darken it.
        var sunny = AnalysisImage.Create(200, 150, (x, y) => y < 50 ? ((byte)225, (byte)225, (byte)220) : ((byte)(170 + (x % 15)), (byte)185, (byte)120));

        var result = AutoTone.Suggest(sunny, EditRecipe.Neutral);

        Assert.Equal(0, result.Named("Natural")!.Recipe.Exposure);
        Assert.Contains("left as shot", result.Named("Natural")!.Reason, StringComparison.Ordinal);
        Assert.NotEqual(result.Named("Natural")!.Recipe, result.Named("Brighter")!.Recipe);
    }

    [Fact]
    public void AutoTone_ClippedHighlights_PullsHighlightsDown()
    {
        // 10 % blown white (like white kit in sun), the rest mid-grey.
        var photo = AnalysisImage.Create(100, 100, (x, y) => y < 10 ? ((byte)255, (byte)255, (byte)255) : ((byte)120, (byte)120, (byte)120));

        var natural = AutoTone.Suggest(photo, EditRecipe.Neutral).Named("Natural")!;

        Assert.True(natural.Recipe.Highlights <= -40, $"highlights {natural.Recipe.Highlights}");
        Assert.Contains("clipped", natural.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoTone_HoldsBackExposure_ToProtectUnclippedHighlights()
    {
        // Dark mid-tones with a bright (not clipped) sky: brightening fully would blow the sky.
        var photo = AnalysisImage.Create(100, 100, (x, y) => y < 30 ? ((byte)235, (byte)235, (byte)235) : ((byte)40, (byte)40, (byte)40));

        var natural = AutoTone.Suggest(photo, EditRecipe.Neutral).Named("Natural")!;

        Assert.Contains("held back", natural.Reason, StringComparison.Ordinal);
        Assert.True(natural.Recipe.Exposure < 0.3);
    }

    [Fact]
    public void AutoTone_BlueCastNeutrals_AreWarmed_AndKeepsNonToneSettings()
    {
        var bluish = AnalysisImage.Create(100, 100, (_, _) => (150, 155, 170));
        var current = EditRecipe.Neutral with { Saturation = 30, Straighten = 2, Crop = new CropRect(0, 0, 0.5, 0.5) };

        var natural = AutoTone.Suggest(bluish, current).Named("Natural")!;

        Assert.True(natural.Recipe.Temperature > 5, $"temperature {natural.Recipe.Temperature}");
        Assert.Equal(30, natural.Recipe.Saturation);
        Assert.Equal(2, natural.Recipe.Straighten);
        Assert.Equal(current.Crop, natural.Recipe.Crop);
    }

    [Fact]
    public void AutoTone_IsDeterministic()
    {
        var photo = AnalysisImage.Create(160, 90, (x, y) => ((byte)(x + y), (byte)(2 * y), (byte)(255 - x)));
        Assert.Equal(AutoTone.Suggest(photo, EditRecipe.Neutral).Options, AutoTone.Suggest(photo, EditRecipe.Neutral).Options);
    }

    // ------------------------------------------------------------------ Auto level

    /// <summary>A sky/ground scene with a hard horizontal horizon, a little texture, tilted by rendering it.</summary>
    private static AnalysisImage Horizon(double tilt, int width = 900, int height = 600)
    {
        var level = AnalysisImage.Create(width, height, (x, y) => y < height * 0.45
            ? ((byte)(190 + (x % 7)), (byte)(205 + (y % 5)), (byte)225)
            : ((byte)(70 + ((x * 3) % 11)), (byte)(110 + (y % 9)), (byte)50));
        return EditRenderer.Render(level, EditRecipe.Neutral with { Straighten = tilt }, null, Ct);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(-3.5)]
    [InlineData(11)]
    public void AutoLevel_FindsTheHorizon_AndUndoesTheTilt(double tilt)
    {
        var result = AutoStraighten.Suggest(Horizon(tilt), EditRecipe.Neutral, Ct);

        var main = result.Named("Main line")!;
        Assert.Equal(-tilt, main.Recipe.Straighten, 0.4);
        Assert.NotNull(main.Guide);
        Assert.Equal(main.Guide!.Y1, main.Guide.Y2, 2); // the guide is level after the correction
        Assert.Equal("Keep as is", result.Options[^1].Name);
        Assert.Equal(0, result.Options[^1].Recipe.Straighten);
    }

    [Fact]
    public void AutoLevel_AlreadyLevel_OffersNothing()
    {
        var result = AutoStraighten.Suggest(Horizon(0), EditRecipe.Neutral, Ct);
        Assert.False(result.HasOptions);
        Assert.Contains("Already level", result.Unavailable, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoLevel_NoLines_OffersNothing()
    {
        var random = new Random(42);
        var noise = AnalysisImage.Create(400, 300, (_, _) => { var v = (byte)random.Next(256); return (v, v, v); });
        var result = AutoStraighten.Suggest(noise, EditRecipe.Neutral, Ct);
        Assert.False(result.HasOptions);
        Assert.Contains("No long straight line", result.Unavailable, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Auto crop

    private static FaceFinding Face(double x, double y, double w, double h) => new((float)x, (float)y, (float)w, (float)h, 0.9f, 120);

    private static void AssertContains(CropRect crop, FaceFinding f, int w, int h, EditRecipe recipe)
    {
        var g = new EditGeometry(w, h, recipe);
        foreach (var (cx, cy) in new[] { (f.X, f.Y), (f.X + f.Width, f.Y + f.Height) })
        {
            var (u, v) = g.ToOutput(cx * w, cy * h);
            Assert.InRange(u, -1e-6, 1 + 1e-6);
            Assert.InRange(v, -1e-6, 1 + 1e-6);
        }
    }

    [Fact]
    public void AutoCrop_SingleFace_ExactRatio_FaceInside_OnUpperThird_MinSize()
    {
        var face = Face(0.62, 0.25, 0.08, 0.12); // a 480 px face: close enough that Tight stays above the minimum size
        var result = AutoCrop.Suggest(6000, 4000, [face], EditRecipe.Neutral, 4.0 / 5, 0.6);

        Assert.Equal(["Tight", "Medium", "Wide"], result.Options.Select(o => o.Name));
        foreach (var option in result.Options)
        {
            var c = option.Recipe.Crop;
            Assert.Equal(0.8, c.Width * 6000 / (c.Height * 4000), 2);
            Assert.True(Math.Max(c.Width * 6000, c.Height * 4000) >= 1999, option.Name);
            AssertContains(c, face, 6000, 4000, option.Recipe);
        }

        // Tight: the face's eye line is on (or near) the upper third.
        var tight = result.Named("Tight")!.Recipe.Crop;
        var eye = 0.25 + (0.12 * 0.45);
        Assert.Equal(1.0 / 3, (eye - tight.Y) / tight.Height, 1);
    }

    [Fact]
    public void AutoCrop_SpectatorFaces_AreIgnored()
    {
        var player = Face(0.30, 0.25, 0.05, 0.08);
        var spectator = Face(0.92, 0.10, 0.008, 0.012); // tiny, far corner
        var tight = AutoCrop.Suggest(6000, 4000, [player, spectator], EditRecipe.Neutral, 1, 0.6).Named("Tight")!.Recipe.Crop;

        Assert.True(tight.X + tight.Width < 0.9, "the crop was not stretched to include the spectator");
    }

    [Fact]
    public void AutoCrop_Group_IsCentredAndKeepsEveryFace()
    {
        FaceFinding[] group = [Face(0.35, 0.3, 0.05, 0.07), Face(0.45, 0.31, 0.05, 0.07), Face(0.55, 0.29, 0.05, 0.07)];
        var result = AutoCrop.Suggest(6000, 4000, group, EditRecipe.Neutral, 16.0 / 9, 0.6);

        Assert.True(result.HasOptions);
        foreach (var option in result.Options)
        {
            foreach (var f in group)
            {
                AssertContains(option.Recipe.Crop, f, 6000, 4000, option.Recipe);
            }
        }
    }

    [Fact]
    public void AutoCrop_RespectsStraightenAndQuarterTurns()
    {
        var face = Face(0.5, 0.3, 0.05, 0.07);
        var current = EditRecipe.Neutral with { QuarterTurns = 1, Straighten = 4 };

        var result = AutoCrop.Suggest(6000, 4000, [face], current, null, 0.6);

        Assert.All(result.Options, o =>
        {
            Assert.Equal(1, o.Recipe.QuarterTurns);
            Assert.Equal(4, o.Recipe.Straighten);
            AssertContains(o.Recipe.Crop, face, 6000, 4000, o.Recipe);
        });
    }

    [Fact]
    public void AutoCrop_NoFaces_OrOnlyLowScores_OffersNothing()
    {
        Assert.Contains("No faces", AutoCrop.Suggest(6000, 4000, [], EditRecipe.Neutral, 1, 0.6).Unavailable, StringComparison.Ordinal);
        var weak = new FaceFinding(0.5f, 0.5f, 0.05f, 0.05f, 0.4f, 100);
        Assert.False(AutoCrop.Suggest(6000, 4000, [weak], EditRecipe.Neutral, 1, 0.6).HasOptions);
    }

    [Fact]
    public void AutoCrop_SmallPhoto_NeverCropsBelowItsOwnSize()
    {
        var result = AutoCrop.Suggest(1600, 1200, [Face(0.4, 0.3, 0.05, 0.07)], EditRecipe.Neutral, 4.0 / 3, 0.6);
        var only = Assert.Single(result.Options); // every variant grows to the whole frame, so duplicates collapse
        Assert.True(only.Recipe.Crop.IsFull || (only.Recipe.Crop.Width > 0.99 && only.Recipe.Crop.Height > 0.99));
    }

}
