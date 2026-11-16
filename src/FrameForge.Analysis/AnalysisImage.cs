namespace FrameForge.Analysis;

/// <summary>
/// An image prepared for analysis: 8-bit BGRA pixels, top-down, stride = width × 4, EXIF orientation already applied.
/// Typically a downscaled copy (see <see cref="AnalysisSettings.MaxPixelSize"/>), never the original file.
/// </summary>
public sealed class AnalysisImage
{
    public AnalysisImage(int width, int height, byte[] bgra)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes of BGRA data, got {bgra.Length}.", nameof(bgra));
        }

        Width = width;
        Height = height;
        Bgra = bgra;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>BGRA bytes. Treat as read-only.</summary>
    public byte[] Bgra { get; }

    /// <summary>
    /// Rec. 709 luma on gamma-encoded values (0–255). A perceptual approximation, sufficient for heuristics.
    /// </summary>
    public float[] ToLuminance()
    {
        var luminance = new float[Width * Height];
        for (int i = 0, p = 0; i < luminance.Length; i++, p += 4)
        {
            luminance[i] = (0.0722f * Bgra[p]) + (0.7152f * Bgra[p + 1]) + (0.2126f * Bgra[p + 2]);
        }

        return luminance;
    }

    /// <summary>Builds an image from a per-pixel colour function. Intended for tests and fixtures.</summary>
    public static AnalysisImage Create(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        ArgumentNullException.ThrowIfNull(pixel);
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = ((y * width) + x) * 4;
                bgra[i] = b;
                bgra[i + 1] = g;
                bgra[i + 2] = r;
                bgra[i + 3] = 255;
            }
        }

        return new AnalysisImage(width, height, bgra);
    }
}
