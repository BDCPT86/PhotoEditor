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

    /// <summary>
    /// Copies a region given in normalised coordinates (0–1). Returns null when the region is smaller than
    /// 8×8 pixels after clamping to the image.
    /// </summary>
    public AnalysisImage? Crop(float x, float y, float width, float height)
    {
        var left = Math.Clamp((int)MathF.Floor(x * Width), 0, Width);
        var top = Math.Clamp((int)MathF.Floor(y * Height), 0, Height);
        var right = Math.Clamp((int)MathF.Ceiling((x + width) * Width), 0, Width);
        var bottom = Math.Clamp((int)MathF.Ceiling((y + height) * Height), 0, Height);
        int w = right - left, h = bottom - top;
        if (w < 8 || h < 8)
        {
            return null;
        }

        var pixels = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
        {
            Buffer.BlockCopy(Bgra, (((top + row) * Width) + left) * 4, pixels, row * w * 4, w * 4);
        }

        return new AnalysisImage(w, h, pixels);
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
