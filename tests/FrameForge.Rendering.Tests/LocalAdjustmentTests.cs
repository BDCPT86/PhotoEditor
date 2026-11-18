using FrameForge.Analysis;
using FrameForge.Core.Editing;
using static FrameForge.Rendering.Tests.TestImages;

namespace FrameForge.Rendering.Tests;

/// <summary>Local adjustments (Milestone 08 phase B, B4): blending, geometry, edges, and preview against export.</summary>
public sealed class LocalAdjustmentTests
{
    private static readonly Guid Id = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EditRecipe WithLocal(EditRecipe recipe, bool invert = false, double exposure = 1, double saturation = 0) => recipe with
    {
        LocalAdjustments = new(
        [
            new LocalAdjustment
            {
                Id = Id,
                Mask = new MaskReference { Kind = MaskKind.Subject, Source = "test@1", Invert = invert },
                Exposure = exposure,
                Saturation = saturation,
            },
        ]),
    };

    private static MaskImage Mask(int w, int h, Func<int, int, byte> value)
    {
        var alpha = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                alpha[(y * w) + x] = value(x, y);
            }
        }

        return new MaskImage(w, h, alpha);
    }

    private static Dictionary<Guid, MaskImage> Masks(MaskImage mask) => new() { [Id] = mask };

    private static AnalysisImage RenderWith(AnalysisImage source, EditRecipe recipe, MaskImage? mask, int? maxLongSide = null) =>
        EditRenderer.Render(source, recipe, mask is null ? null : Masks(mask), maxLongSide, Ct);

    private static void AssertClose(AnalysisImage expected, AnalysisImage actual, int tolerance)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        var worst = 0;
        for (var i = 0; i < expected.Bgra.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(expected.Bgra[i] - actual.Bgra[i]));
        }

        Assert.True(worst <= tolerance, $"Largest difference {worst} > {tolerance}");
    }

    // ------------------------------------------------------------------ neutral and bit-identical paths

    [Fact]
    public void RecipeWithoutLocals_IsBitIdenticalToTheV1Path()
    {
        var source = Gradient(64, 48);
        var recipe = EditRecipe.Neutral with { Exposure = 0.4, Contrast = 20, Crop = new CropRect(0.1, 0.1, 0.8, 0.8) };

        Assert.Equal(Render(source, recipe).Bgra, EditRenderer.Render(source, recipe, Masks(Mask(8, 6, (_, _) => 255)), null, Ct).Bgra);
        Assert.Equal(source.Bgra, EditRenderer.Render(source, EditRecipe.Neutral, null, null, Ct).Bgra);
    }

    [Fact]
    public void LocalWithoutItsMask_IsLeftOut_AndTheRestRenders()
    {
        var source = Gradient(64, 48);
        var global = EditRecipe.Neutral with { Exposure = 0.5 };

        Assert.Equal(Render(source, global).Bgra, RenderWith(source, WithLocal(global), mask: null).Bgra);
        Assert.Equal(Render(source, global).Bgra, EditRenderer.Render(source, WithLocal(global), new Dictionary<Guid, MaskImage>(), null, Ct).Bgra);
    }

    [Fact]
    public void LocalWithNoEffect_ChangesNothing() =>
        Assert.Equal(Gradient(64, 48).Bgra, RenderWith(Gradient(64, 48), WithLocal(EditRecipe.Neutral, exposure: 0), Mask(8, 6, (_, _) => 255)).Bgra);

    // ------------------------------------------------------------------ blending

    [Fact]
    public void FullMask_EqualsTheSameSettingsAppliedGlobally()
    {
        var source = Gradient(64, 48);

        var local = RenderWith(source, WithLocal(EditRecipe.Neutral, exposure: 0.7, saturation: -30), Mask(16, 12, (_, _) => 255));

        AssertClose(Render(source, EditRecipe.Neutral with { Exposure = 0.7, Saturation = -30 }), local, 1);
    }

    [Fact]
    public void EmptyMask_ChangesNothing() =>
        AssertClose(Gradient(64, 48), RenderWith(Gradient(64, 48), WithLocal(EditRecipe.Neutral), Mask(16, 12, (_, _) => 0)), 0);

    [Fact]
    public void InvertedEmptyMask_IsTheWholePhoto()
    {
        var source = Gradient(64, 48);

        AssertClose(Render(source, EditRecipe.Neutral with { Exposure = 1 }), RenderWith(source, WithLocal(EditRecipe.Neutral, invert: true), Mask(16, 12, (_, _) => 0)), 1);
    }

    [Fact]
    public void LocalIsAppliedAfterTheGlobalSettings_InLinearLight()
    {
        // +1 EV globally and +1 EV locally on a full mask is +2 EV.
        var source = Solid(32, 24, 40, 40, 40);

        var both = RenderWith(source, WithLocal(EditRecipe.Neutral with { Exposure = 1 }), Mask(8, 6, (_, _) => 255));

        AssertClose(Render(source, EditRecipe.Neutral with { Exposure = 2 }), both, 1);
    }

    [Fact]
    public void HalfMask_BlendsHalfway_InLinearLight()
    {
        // On a flat image the guided filter keeps a flat mask, so α = 128/255 everywhere.
        var source = Solid(32, 24, 60, 60, 60);

        var output = RenderWith(source, WithLocal(EditRecipe.Neutral, exposure: 1), Mask(8, 6, (_, _) => 128));

        var before = Linear(60);
        var expected = before + ((128 / 255.0) * ((2 * before) - before));
        Assert.InRange(Linear(Pixel(output, 16, 12).G), expected * 0.98, expected * 1.02);
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>Flat grey, left half selected.</summary>
    private static MaskImage LeftHalf => Mask(40, 30, (x, _) => x < 20 ? (byte)255 : (byte)0);

    [Fact]
    public void Mask_FollowsTheCrop()
    {
        var source = Solid(80, 60, 70, 70, 70);

        var rightHalf = RenderWith(source, WithLocal(EditRecipe.Neutral with { Crop = new CropRect(0.55, 0, 0.45, 1) }), LeftHalf);
        var leftHalf = RenderWith(source, WithLocal(EditRecipe.Neutral with { Crop = new CropRect(0, 0, 0.45, 1) }), LeftHalf);

        Assert.Equal(70, Pixel(rightHalf, rightHalf.Width / 2, 30).G);
        Assert.True(Pixel(leftHalf, leftHalf.Width / 2, 30).G > 90);
    }

    [Fact]
    public void Mask_FollowsQuarterTurns()
    {
        var source = Solid(80, 60, 70, 70, 70);

        var turned = RenderWith(source, WithLocal(EditRecipe.Neutral with { QuarterTurns = 2 }), LeftHalf);

        // Turned 180°, the photo's left half is on the right.
        Assert.Equal(70, Pixel(turned, 10, 30).G);
        Assert.True(Pixel(turned, 70, 30).G > 90);
    }

    [Fact]
    public void Mask_FollowsStraighten()
    {
        // The top half is selected; after straightening by 10° the line between them tilts the same way as the photo.
        var source = AnalysisImage.Create(200, 100, (_, y) => y < 50 ? ((byte)90, (byte)90, (byte)90) : ((byte)90, (byte)90, (byte)91));
        var topHalf = Mask(200, 100, (_, y) => y < 50 ? (byte)255 : (byte)0);
        var recipe = WithLocal(EditRecipe.Neutral with { Straighten = 10 });

        var output = RenderWith(source, recipe, topHalf);
        var geometry = new EditGeometry(200, 100, recipe);

        for (var x = 10; x < output.Width - 10; x += 20)
        {
            for (var y = 5; y < output.Height - 5; y += 10)
            {
                var (_, sy) = geometry.ToSource((x + 0.5) / output.Width, (y + 0.5) / output.Height);
                if (Math.Abs(sy - 50) < 12)
                {
                    continue; // near the edge, where the mask is feathered
                }

                Assert.Equal(sy < 50, Pixel(output, x, y).G > 110);
            }
        }
    }

    // ------------------------------------------------------------------ edges

    /// <summary>A dark subject (x &lt; 512) on a bright background, at the working grid's scale (1024 px wide).</summary>
    private static AnalysisImage SubjectOnBright => AnalysisImage.Create(1024, 64, (x, _) => x < 512 ? ((byte)50, (byte)50, (byte)50) : ((byte)200, (byte)200, (byte)200));

    /// <summary>The mask strength the renderer used at column x, recovered from a +0.5 EV local adjustment.</summary>
    private static double AlphaAt(AnalysisImage output, int x)
    {
        var before = Linear(Pixel(SubjectOnBright, x, 32).G);
        return ((Linear(Pixel(output, x, 32).G) / before) - 1) / (Math.Sqrt(2) - 1);
    }

    private static AnalysisImage RenderEdge(int maskEdge) =>
        RenderWith(SubjectOnBright, WithLocal(EditRecipe.Neutral, exposure: 0.5), Mask(1024, 64, (x, _) => x < maskEdge ? (byte)255 : (byte)0));

    /// <summary>The column after which the mask drops the most.</summary>
    private static int SteepestDrop(AnalysisImage output) =>
        Enumerable.Range(480, 64).MaxBy(x => AlphaAt(output, x) - AlphaAt(output, x + 1));

    [Fact]
    public void MaskThatStopsShort_EdgeMovesToThePhotosEdge_AndTheGapIsPartlyFilled()
    {
        // The mask stops 4 px before the subject's edge (x = 512), as a coarse mask does.
        var output = RenderEdge(508);

        Assert.Equal(511, SteepestDrop(output));            // the transition sits on the photo's edge
        Assert.InRange(AlphaAt(output, 510), 0.25, 0.75);    // the gap is softened, not left at 0
        Assert.InRange(AlphaAt(output, 514), -0.02, 0.02);   // the background is untouched
        Assert.InRange(AlphaAt(output, 400), 0.98, 1.02);    // the subject well inside is fully adjusted
    }

    [Fact]
    public void MaskThatSpillsOver_HaloIsSoftened_AndTheEdgeMovesToThePhotosEdge()
    {
        // The mask reaches 4 px into the background, where a hard mask would draw a bright halo.
        var output = RenderEdge(516);

        Assert.Equal(511, SteepestDrop(output));
        Assert.InRange(AlphaAt(output, 510), 0.97, 1.02);    // the subject is fully adjusted up to its edge
        Assert.InRange(AlphaAt(output, 514), 0, 0.6);        // a hard mask would be 1 here
        Assert.InRange(AlphaAt(output, 530), -0.02, 0.1);
    }

    [Fact]
    public void Coefficients_KeepAFlatMaskFlat_AndFollowAStepInTheGuide()
    {
        const int w = 40, h = 10;
        var guide = new float[w * h];
        var flat = new float[w * h];
        var step = new float[w * h];
        for (var i = 0; i < guide.Length; i++)
        {
            guide[i] = i % w < 20 ? 0.2f : 0.8f;
            flat[i] = 0.6f;
            step[i] = i % w < 20 ? 1f : 0f;
        }

        var (a, b) = MaskField.GuidedCoefficients(guide, flat, w, h, 3, 1e-3f);
        Assert.All(Enumerable.Range(0, guide.Length), i => Assert.Equal(0.6f, (a[i] * guide[i]) + b[i], 3));

        var (a2, b2) = MaskField.GuidedCoefficients(guide, step, w, h, 3, 1e-3f);
        Assert.InRange((a2[19] * guide[19]) + b2[19], 0.95f, 1.05f); // last subject pixel
        Assert.InRange((a2[20] * guide[20]) + b2[20], -0.05f, 0.05f); // first background pixel
    }

    [Fact]
    public void BoxMean_IsTheWindowAverage_ClippedAtTheBorders()
    {
        float[] values = [1, 2, 3, 4, 5];

        Assert.Equal([1.5f, 2f, 3f, 4f, 4.5f], MaskField.BoxMean(values, 5, 1, 1));
    }

    // ------------------------------------------------------------------ preview and export

    [Fact]
    public void Preview_MatchesTheDownscaledExport()
    {
        // A soft subject on a gradient, with a mask computed at a different size from either render.
        var full = AnalysisImage.Create(400, 300, (x, y) =>
        {
            var inside = ((x - 160) * (x - 160)) + ((y - 150) * (y - 150)) < 80 * 80;
            return inside ? ((byte)(60 + (y / 6)), (byte)70, (byte)90) : ((byte)(150 + (x / 8)), (byte)(170 + (y / 10)), (byte)190);
        });
        var mask = Mask(160, 120, (x, y) => ((x - 64) * (x - 64)) + ((y - 60) * (y - 60)) < 32 * 32 ? (byte)255 : (byte)0);
        var recipe = WithLocal(EditRecipe.Neutral with { Straighten = 3, Crop = new CropRect(0.05, 0.05, 0.9, 0.9), Contrast = 15 }, exposure: 0.8, saturation: 20);

        var export = RenderWith(full, recipe, mask);
        var preview = RenderWith(BoxDown(full, 2), recipe, mask);
        var exportDown = BoxDown(export, 2);

        Assert.Equal((preview.Width, preview.Height), (exportDown.Width, exportDown.Height));
        double total = 0;
        for (var i = 0; i < preview.Bgra.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                total += Math.Abs(preview.Bgra[i + c] - exportDown.Bgra[i + c]);
            }
        }

        Assert.True(total / (preview.Width * preview.Height * 3) < 1.5, $"Mean difference {total / (preview.Width * preview.Height * 3):0.00} levels");
    }

    // ------------------------------------------------------------------ golden pixels

    [Fact]
    public void Golden_LocalAdjustmentPixels()
    {
        // Values recorded with render-v2 on 2026-11-18. If they move, review real photos and bump EditRenderer.Version.
        var source = Gradient(64, 48);
        var mask = Mask(32, 24, (x, y) => x < 16 && y < 12 ? (byte)255 : (byte)0);
        var output = RenderWith(source, WithLocal(EditRecipe.Neutral with { Temperature = 20 }, exposure: 0.8, saturation: -40), mask);

        int[] expected = [GoldenValues[0], GoldenValues[1], GoldenValues[2], GoldenValues[3], GoldenValues[4], GoldenValues[5]];
        var (r1, g1, b1) = Pixel(output, 8, 6);   // inside
        var (r2, g2, b2) = Pixel(output, 56, 40); // outside
        int[] actual = [r1, g1, b1, r2, g2, b2];

        // Cross-checks that do not depend on the recorded numbers: inside, the same as applying everything globally;
        // outside, the same as the global settings alone.
        var everything = Pixel(Render(source, EditRecipe.Neutral with { Temperature = 20, Exposure = 0.8, Saturation = -40 }), 8, 6);
        var globalOnly = Pixel(Render(source, EditRecipe.Neutral with { Temperature = 20 }), 56, 40);
        Assert.True(Math.Abs(everything.R - r1) <= 1 && Math.Abs(everything.G - g1) <= 1 && Math.Abs(everything.B - b1) <= 1, $"inside {string.Join(",", actual)} vs {everything}");
        Assert.Equal(globalOnly, (r2, g2, b2));

        for (var i = 0; i < actual.Length; i++)
        {
            Assert.True(Math.Abs(actual[i] - expected[i]) <= 1, $"value {i} is {actual[i]}, expected {expected[i]} (all: {string.Join(", ", actual)})");
        }
    }

    private static readonly int[] GoldenValues = [88, 91, 176, 205, 190, 67];
}
