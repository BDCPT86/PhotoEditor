using FrameForge.Analysis;

namespace FrameForge.Analysis.Tests;

/// <summary>M09 C2: the overlay paints exactly the pixels the histogram counts as clipped, and nothing else.</summary>
public sealed class ClippingPainterTests
{
    private static AnalysisImage Image(params (byte R, byte G, byte B)[] pixels) =>
        new(pixels.Length, 1, pixels.SelectMany(p => new[] { p.B, p.G, p.R, (byte)255 }).ToArray());

    private static (byte R, byte G, byte B) At(AnalysisImage image, int x) =>
        (image.Bgra[(x * 4) + 2], image.Bgra[(x * 4) + 1], image.Bgra[x * 4]);

    [Fact]
    public void PaintsHighlightsRed_ShadowsBlue_AndLeavesTheRest()
    {
        var source = Image((255, 10, 10), (254, 254, 254), (253, 253, 253), (1, 0, 1), (2, 0, 0), (0, 0, 0), (120, 80, 40));

        var painted = ClippingPainter.Paint(source);

        var red = (ClippingPainter.HighlightColour.R, ClippingPainter.HighlightColour.G, ClippingPainter.HighlightColour.B);
        var blue = (ClippingPainter.ShadowColour.R, ClippingPainter.ShadowColour.G, ClippingPainter.ShadowColour.B);
        Assert.Equal(red, At(painted, 0));
        Assert.Equal(red, At(painted, 1));
        Assert.Equal(((byte)253, (byte)253, (byte)253), At(painted, 2));
        Assert.Equal(blue, At(painted, 3));
        Assert.Equal(((byte)2, (byte)0, (byte)0), At(painted, 4)); // one channel above 1: not a clipped shadow
        Assert.Equal(blue, At(painted, 5));
        Assert.Equal(((byte)120, (byte)80, (byte)40), At(painted, 6));
    }

    [Fact]
    public void TheSourceIsNotChanged()
    {
        var source = Image((255, 255, 255), (0, 0, 0));
        var before = (byte[])source.Bgra.Clone();

        ClippingPainter.Paint(source);

        Assert.Equal(before, source.Bgra);
    }

    [Fact]
    public void PaintsExactlyWhatTheHistogramCountsAsClipped()
    {
        const int width = 200, height = 100;
        var data = new byte[width * height * 4];
        new Random(11).NextBytes(data);
        for (var p = 0; p < data.Length; p += 4)
        {
            // Push a share of pixels to the ends, so both kinds of clipping occur.
            if (data[p] < 20) { data[p] = data[p + 1] = data[p + 2] = 0; }
            if (data[p] > 240) { data[p + 2] = 255; }
        }

        var source = new AnalysisImage(width, height, data);
        var histogram = ImageHistogram.Compute(source.Bgra, width, height, width * 4);
        var painted = ClippingPainter.Paint(source);

        long red = 0, blue = 0;
        for (var p = 0; p < painted.Bgra.Length; p += 4)
        {
            var changed = painted.Bgra[p] != data[p] || painted.Bgra[p + 1] != data[p + 1] || painted.Bgra[p + 2] != data[p + 2];
            var isRed = painted.Bgra[p + 2] == ClippingPainter.HighlightColour.R && painted.Bgra[p] == ClippingPainter.HighlightColour.B;
            var isBlue = painted.Bgra[p] == ClippingPainter.ShadowColour.B && painted.Bgra[p + 2] == ClippingPainter.ShadowColour.R;
            red += changed && isRed ? 1 : 0;
            blue += changed && isBlue ? 1 : 0;
        }

        Assert.True(histogram.HighlightClipped > 0 && histogram.ShadowClipped > 0);
        Assert.InRange(histogram.HighlightClipped - red, 0, histogram.HighlightClipped / 50); // a pixel already that colour can't show as "changed"
        Assert.InRange(histogram.ShadowClipped - blue, 0, histogram.ShadowClipped / 50);
    }
}
