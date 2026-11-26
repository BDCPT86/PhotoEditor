namespace FrameForge.Analysis;

/// <summary>
/// Red, green, blue and luminance counts in 256 bins, with the share of clipped pixels (M09). Computed from the image
/// on screen for the viewer's and Develop's histogram. Display only: nothing is stored.
/// </summary>
/// <remarks>
/// <para>Luminance uses the Rec. 709 weights of <see cref="ExposureAnalyzer"/>, in 16-bit fixed point and rounded to the
/// nearest level.</para>
/// <para>Clipping uses the analysis badge's levels (<see cref="ExposureAnalyzer.HighlightLevel"/>,
/// <see cref="ExposureAnalyzer.ShadowLevel"/>): a highlight is any channel at 254–255, a shadow every channel at 0–1,
/// so the histogram, the clipping overlay and the ◐ badge agree.</para>
/// </remarks>
public sealed class ImageHistogram
{
    public const int Bins = 256;

    // 0.2126, 0.7152, 0.0722 × 65,536 (summing to 65,536, so white stays 255).
    private const int RedWeight = 13933;
    private const int GreenWeight = 46871;
    private const int BlueWeight = 4732;

    private ImageHistogram(int[] red, int[] green, int[] blue, int[] luminance, long pixels, long highlights, long shadows)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Luminance = luminance;
        PixelCount = pixels;
        HighlightClipped = highlights;
        ShadowClipped = shadows;
    }

    public IReadOnlyList<int> Red { get; }

    public IReadOnlyList<int> Green { get; }

    public IReadOnlyList<int> Blue { get; }

    public IReadOnlyList<int> Luminance { get; }

    public long PixelCount { get; }

    /// <summary>Pixels with any channel at 254–255.</summary>
    public long HighlightClipped { get; }

    /// <summary>Pixels with every channel at 0–1.</summary>
    public long ShadowClipped { get; }

    public double HighlightClippedPercent => PixelCount == 0 ? 0 : 100.0 * HighlightClipped / PixelCount;

    public double ShadowClippedPercent => PixelCount == 0 ? 0 : 100.0 * ShadowClipped / PixelCount;

    /// <summary>The luminance level of one pixel, 0–255.</summary>
    public static int LuminanceOf(byte red, byte green, byte blue) =>
        ((RedWeight * red) + (GreenWeight * green) + (BlueWeight * blue) + 32768) >> 16;

    /// <summary>Counts every pixel of a 32-bit BGRA (or BGR32) image; the alpha byte is ignored.</summary>
    /// <param name="stride">Bytes per row, at least <c>width × 4</c>.</param>
    public static ImageHistogram Compute(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (height > 0 && bgra.Length < ((long)(height - 1) * stride) + (width * 4L))
        {
            throw new ArgumentException("The buffer is smaller than the image.", nameof(bgra));
        }

        var red = new int[Bins];
        var green = new int[Bins];
        var blue = new int[Bins];
        var luminance = new int[Bins];
        long highlights = 0, shadows = 0;

        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * stride, width * 4);
            for (var p = 0; p < row.Length; p += 4)
            {
                byte b = row[p], g = row[p + 1], r = row[p + 2];
                red[r]++;
                green[g]++;
                blue[b]++;
                luminance[LuminanceOf(r, g, b)]++;

                var max = Math.Max(r, Math.Max(g, b));
                if (max >= ExposureAnalyzer.HighlightLevel)
                {
                    highlights++;
                }

                if (max <= ExposureAnalyzer.ShadowLevel)
                {
                    shadows++;
                }
            }
        }

        return new ImageHistogram(red, green, blue, luminance, (long)width * height, highlights, shadows);
    }
}
