using FrameForge.Analysis;
using FrameForge.Core.Editing;
using static FrameForge.Rendering.Tests.TestImages;

namespace FrameForge.Rendering.Tests;

public sealed class GeometryTests
{
    /// <summary>6×4 image, every pixel a unique colour encoding its coordinates.</summary>
    private static AnalysisImage Coded() => AnalysisImage.Create(6, 4, (x, y) => ((byte)(x * 40), (byte)(y * 60), 7));

    [Fact]
    public void QuarterTurn_RotatesClockwise_AndSwapsSize()
    {
        var source = Coded();
        var turned = Render(source, EditRecipe.Neutral with { QuarterTurns = 1 });

        Assert.Equal((4, 6), (turned.Width, turned.Height));
        // Clockwise: the source's bottom-left ends up top-left, and its top-left ends up top-right.
        Assert.Equal(Pixel(source, 0, 3), Pixel(turned, 0, 0));
        Assert.Equal(Pixel(source, 0, 0), Pixel(turned, 3, 0));
        Assert.Equal(Pixel(source, 5, 3), Pixel(turned, 0, 5));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OtherTurns_MapCornersCorrectly(int turns)
    {
        var source = Coded();
        var turned = Render(source, EditRecipe.Neutral with { QuarterTurns = turns });
        if (turns == 2)
        {
            Assert.Equal(Pixel(source, 5, 3), Pixel(turned, 0, 0));
        }
        else
        {
            Assert.Equal((4, 6), (turned.Width, turned.Height));
            Assert.Equal(Pixel(source, 5, 0), Pixel(turned, 0, 0)); // counter-clockwise: top-right → top-left
        }
    }

    [Fact]
    public void Crop_CopiesExactlyTheChosenRegion()
    {
        var source = Gradient(40, 20);
        var cropped = Render(source, EditRecipe.Neutral with { Crop = new CropRect(0.5, 0.25, 0.5, 0.5) });

        Assert.Equal((20, 10), (cropped.Width, cropped.Height));
        Assert.Equal(Pixel(source, 20, 5), Pixel(cropped, 0, 0));
        Assert.Equal(Pixel(source, 39, 14), Pixel(cropped, 19, 9));
    }

    [Fact]
    public void InscribedScale_IsOneWhenLevel_AndShrinksWithAngle()
    {
        Assert.Equal(1, EditGeometry.InscribedScale(300, 200, 0), 9);
        var s10 = EditGeometry.InscribedScale(300, 200, 10 * Math.PI / 180);
        var s30 = EditGeometry.InscribedScale(300, 200, 30 * Math.PI / 180);
        Assert.InRange(s10, 0.7, 0.99);
        Assert.True(s30 < s10);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-25)]
    [InlineData(45)]
    public void Straighten_NeverSamplesOutsideThePhoto(double angle)
    {
        var geometry = new EditGeometry(300, 200, EditRecipe.Neutral with { Straighten = angle });
        foreach (var (u, v) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0), (0.5, 0.5) })
        {
            var (x, y) = geometry.ToSource(u, v);
            Assert.InRange(x, -1e-6, 300 + 1e-6);
            Assert.InRange(y, -1e-6, 200 + 1e-6);
        }
    }

    [Fact]
    public void Straighten_KeepsTheAspectRatio()
    {
        var geometry = new EditGeometry(300, 200, EditRecipe.Neutral with { Straighten = 12 });
        Assert.Equal(1.5, geometry.OutputWidth / geometry.OutputHeight, 6);
    }

    [Fact]
    public void Straighten_RotatesClockwiseForPositiveAngles()
    {
        // A clockwise picture rotation shows source content from the left of centre at the top of the output.
        var geometry = new EditGeometry(300, 200, EditRecipe.Neutral with { Straighten = 20 });
        var (x, _) = geometry.ToSource(0.5, 0.0);
        Assert.True(x < 150, $"top-centre comes from x={x:0.0}");
    }

    [Fact]
    public void OutputSize_LimitsLongSide_NeverUpscales()
    {
        var geometry = new EditGeometry(4000, 3000, EditRecipe.Neutral);
        Assert.Equal((1000, 750), geometry.OutputSize(1000));
        Assert.Equal((4000, 3000), geometry.OutputSize(10_000));
        Assert.Equal((4000, 3000), geometry.OutputSize(null));
    }

    [Fact]
    public void DecodeLongSide_AccountsForTheCrop()
    {
        var recipe = EditRecipe.Neutral with { Crop = new CropRect(0, 0, 0.5, 0.5) };
        // The crop is 2000×1500 of a 4000×3000 photo; a 1000 px output needs the photo at 2000 px.
        Assert.Equal(2000, EditGeometry.DecodeLongSide(4000, 3000, recipe, 1000));
        Assert.Null(EditGeometry.DecodeLongSide(4000, 3000, recipe, 3000)); // would need upscaling: decode full
        Assert.Null(EditGeometry.DecodeLongSide(4000, 3000, recipe, null));
    }

    [Fact]
    public void PreviewAndExport_Agree()
    {
        // The same recipe on a 4× smaller source (preview) must match the full render downscaled (export).
        var full = Gradient(400, 240);
        var recipe = EditRecipe.Neutral with { Exposure = 0.4, Contrast = 30, Shadows = 40, Saturation = 20, Temperature = 15, Straighten = 7, Crop = new CropRect(0.1, 0.1, 0.8, 0.8) };

        var export = BoxDown(Render(full, recipe), 4);
        var preview = Render(BoxDown(full, 4), recipe, maxLongSide: export.Width);

        Assert.Equal((export.Width, export.Height), (preview.Width, preview.Height));
        double total = 0;
        for (var i = 0; i < export.Bgra.Length; i++)
        {
            total += Math.Abs(export.Bgra[i] - preview.Bgra[i]);
        }

        Assert.True(total / export.Bgra.Length < 2.0, $"mean difference {total / export.Bgra.Length:0.00} levels");
    }

    [Fact]
    public void Render_HonoursCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            EditRenderer.Render(Gradient(64, 64), EditRecipe.Neutral with { Exposure = 1, Straighten = 3 }, null, cts.Token));
    }
}
