namespace FrameForge.Rendering;

/// <summary>
/// A selection mask over the upright photo before crop: one byte per pixel, 0 = outside, 255 = fully inside,
/// top-down, at analysis resolution (not the output size). Masks are derived data, cached on disk, never stored in the
/// recipe or the database. Inversion (Background) is applied where the mask is used, not here.
/// </summary>
public sealed class MaskImage
{
    public MaskImage(int width, int height, byte[] alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(alpha);
        if (alpha.Length != width * height)
        {
            throw new ArgumentException($"Expected {width * height} mask values, got {alpha.Length}.", nameof(alpha));
        }

        Width = width;
        Height = height;
        Alpha = alpha;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>One byte per pixel, row by row. Treat as read-only.</summary>
    public byte[] Alpha { get; }

    /// <summary>Share of the image inside the mask (0–1), counting partial values.</summary>
    public double Coverage()
    {
        long sum = 0;
        foreach (var a in Alpha)
        {
            sum += a;
        }

        return sum / (255.0 * Alpha.Length);
    }
}
