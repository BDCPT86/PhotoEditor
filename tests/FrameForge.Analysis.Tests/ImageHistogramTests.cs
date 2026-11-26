using System.Diagnostics;
using FrameForge.Analysis;

namespace FrameForge.Analysis.Tests;

/// <summary>M09 C1: the histogram matches a straightforward reference count, and its clipping agrees with the badge.</summary>
public sealed class ImageHistogramTests
{
    private static byte[] Bgra(params (byte R, byte G, byte B)[] pixels) =>
        pixels.SelectMany(p => new[] { p.B, p.G, p.R, (byte)255 }).ToArray();

    [Fact]
    public void CountsEachChannel_AndLuminance()
    {
        var histogram = ImageHistogram.Compute(Bgra((255, 0, 0), (0, 255, 0), (0, 0, 255), (128, 128, 128)), 4, 1, 16);

        Assert.Equal(4, histogram.PixelCount);
        Assert.Equal(2, histogram.Red[0]);
        Assert.Equal(1, histogram.Red[255]);
        Assert.Equal(1, histogram.Red[128]);
        Assert.Equal(1, histogram.Green[255]);
        Assert.Equal(1, histogram.Blue[255]);
        Assert.Equal(1, histogram.Luminance[54]);  // 0.2126 × 255
        Assert.Equal(1, histogram.Luminance[182]); // 0.7152 × 255
        Assert.Equal(1, histogram.Luminance[18]);  // 0.0722 × 255
        Assert.Equal(1, histogram.Luminance[128]); // grey stays grey
    }

    [Theory]
    [InlineData(255, 255, 255, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 200, 30, 147)]  // 2.13 + 143.04 + 2.17 = 147.33
    public void Luminance_IsRec709_RoundedToTheNearestLevel(byte r, byte g, byte b, int expected) =>
        Assert.Equal(expected, ImageHistogram.LuminanceOf(r, g, b));

    [Fact]
    public void Clipping_UsesTheBadgesLevels()
    {
        // Highlights: any channel at 254–255. Shadows: every channel at 0–1.
        var histogram = ImageHistogram.Compute(
            Bgra((254, 10, 10), (255, 255, 255), (253, 253, 253), (1, 1, 0), (2, 0, 0), (0, 0, 0), (100, 100, 100), (10, 254, 10)),
            8, 1, 32);

        Assert.Equal(3, histogram.HighlightClipped);
        Assert.Equal(2, histogram.ShadowClipped);
        Assert.Equal(37.5, histogram.HighlightClippedPercent);
        Assert.Equal(25, histogram.ShadowClippedPercent);
    }

    [Fact]
    public void RowPadding_IsSkipped()
    {
        // 2 × 2 pixels with 4 bytes of padding per row filled with white, which must not be counted.
        var data = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255 };

        var histogram = ImageHistogram.Compute(data, 2, 2, 12);

        Assert.Equal(4, histogram.Luminance[0]);
        Assert.Equal(0, histogram.Luminance[255]);
    }

    [Fact]
    public void MatchesAReferenceCount_OnANoiseImage()
    {
        const int width = 300, height = 200;
        var data = new byte[width * height * 4];
        new Random(7).NextBytes(data);

        var histogram = ImageHistogram.Compute(data, width, height, width * 4);

        var reference = new int[256];
        for (var p = 0; p < data.Length; p += 4)
        {
            var luma = (0.2126 * data[p + 2]) + (0.7152 * data[p + 1]) + (0.0722 * data[p]);
            reference[(int)Math.Round(luma, MidpointRounding.AwayFromZero)]++;
        }

        // Fixed point may put a pixel sitting exactly on a half level in the neighbouring bin; allow 0.1 % of pixels.
        var differences = reference.Zip(histogram.Luminance, (a, b) => Math.Abs(a - b)).Sum();
        Assert.True(differences <= width * height / 1000, $"{differences} pixels in other bins");
        Assert.Equal(width * height, histogram.Red.Sum());
        Assert.Equal(width * height, histogram.Luminance.Sum());
    }

    [Fact]
    public void AScreenSizedImage_TakesWellUnder100Ms()
    {
        // 2560 × 1440: a large viewer at full size; the budget is "within 100 ms of an edit at screen size".
        const int width = 2560, height = 1440;
        var data = new byte[width * height * 4];
        new Random(3).NextBytes(data);
        ImageHistogram.Compute(data, width, height, width * 4); // warm up

        var best = long.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            ImageHistogram.Compute(data, width, height, width * 4);
            best = Math.Min(best, stopwatch.ElapsedMilliseconds);
        }

        // The brief's budget. Measured on 2026-11-26: 26 ms in Release, 36 ms in Debug on its own; the best of five runs keeps the
        // other test projects, which run at the same time, from deciding the result.
        Assert.True(best < 100, $"took {best} ms at best");
    }

    [Fact]
    public void AnEmptyImage_HasNoPixelsAndNoClipping()
    {
        var histogram = ImageHistogram.Compute([], 0, 0, 0);

        Assert.Equal(0, histogram.PixelCount);
        Assert.Equal(0, histogram.HighlightClippedPercent);
    }
}
