using FrameForge.Analysis;

namespace FrameForge.AI;

/// <summary>Small, deterministic image helpers for model preprocessing (bilinear resampling on BGRA).</summary>
internal static class ImageOps
{
    /// <summary>Bilinear resize to exactly <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static AnalysisImage Resize(AnalysisImage source, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        int sw = source.Width, sh = source.Height;
        var src = source.Bgra;
        var dst = new byte[width * height * 4];
        var scaleX = (float)sw / width;
        var scaleY = (float)sh / height;

        for (var y = 0; y < height; y++)
        {
            var fy = Math.Max(0, ((y + 0.5f) * scaleY) - 0.5f);
            var y0 = Math.Min((int)fy, sh - 1);
            var y1 = Math.Min(y0 + 1, sh - 1);
            var wy = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Max(0, ((x + 0.5f) * scaleX) - 0.5f);
                var x0 = Math.Min((int)fx, sw - 1);
                var x1 = Math.Min(x0 + 1, sw - 1);
                var wx = fx - x0;
                int i00 = ((y0 * sw) + x0) * 4, i01 = ((y0 * sw) + x1) * 4, i10 = ((y1 * sw) + x0) * 4, i11 = ((y1 * sw) + x1) * 4;
                var d = ((y * width) + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var top = src[i00 + c] + ((src[i01 + c] - src[i00 + c]) * wx);
                    var bottom = src[i10 + c] + ((src[i11 + c] - src[i10 + c]) * wx);
                    dst[d + c] = (byte)Math.Clamp((int)MathF.Round(top + ((bottom - top) * wy)), 0, 255);
                }
            }
        }

        return new AnalysisImage(width, height, dst);
    }

    /// <summary>Scales so the longest side equals <paramref name="longest"/> (aspect preserved).</summary>
    public static AnalysisImage FitLongest(AnalysisImage source, int longest)
    {
        var scale = (float)longest / Math.Max(source.Width, source.Height);
        return Resize(source, Math.Max(1, (int)MathF.Round(source.Width * scale)), Math.Max(1, (int)MathF.Round(source.Height * scale)));
    }
}
