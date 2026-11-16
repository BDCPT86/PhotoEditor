using FrameForge.Analysis;
using FrameForge.Core.Editing;

namespace FrameForge.Rendering;

/// <summary>
/// Renders a recipe onto an oriented 8-bit BGRA image. Preview and export both call this; they differ only in the
/// source resolution and output size. Pure and deterministic; parallel over rows. Never modifies <c>source</c>.
/// </summary>
public static class EditRenderer
{
    /// <summary>Version of the rendering maths, recorded with cached edited thumbnails.</summary>
    public const string Version = "render-v1";

    /// <param name="source">The photo, upright (EXIF orientation applied), at any resolution.</param>
    /// <param name="recipe">A valid recipe.</param>
    /// <param name="maxLongSide">Optional limit on the output's longest side; never upscales.</param>
    public static AnalysisImage Render(AnalysisImage source, EditRecipe recipe, int? maxLongSide = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.EnsureValid();

        var geometry = new EditGeometry(source.Width, source.Height, recipe);
        var (width, height) = geometry.OutputSize(maxLongSide);
        var identity = !recipe.HasGeometry && width == source.Width && height == source.Height;

        var output = identity ? (byte[])source.Bgra.Clone() : Resample(source, geometry, width, height, cancellationToken);
        if (recipe.HasTone || recipe.HasColour)
        {
            ApplyTone(output, width, height, new ToneOps(recipe), cancellationToken);
        }

        return new AnalysisImage(width, height, output);
    }

    private static byte[] Resample(AnalysisImage source, EditGeometry geometry, int width, int height, CancellationToken cancellationToken)
    {
        var src = source.Bgra;
        int sw = source.Width, sh = source.Height;
        var dst = new byte[width * height * 4];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var v = (y + 0.5) / height;
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var (sx, sy) = geometry.ToSource((x + 0.5) / width, v);

                // Bilinear, pixel centres at +0.5, clamped at the edges.
                var fx = Math.Clamp(sx - 0.5, 0, sw - 1);
                var fy = Math.Clamp(sy - 0.5, 0, sh - 1);
                int x0 = (int)fx, y0 = (int)fy;
                int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                float wx = (float)(fx - x0), wy = (float)(fy - y0);
                int i00 = ((y0 * sw) + x0) * 4, i01 = ((y0 * sw) + x1) * 4, i10 = ((y1 * sw) + x0) * 4, i11 = ((y1 * sw) + x1) * 4;
                var d = row + (x * 4);
                for (var c = 0; c < 3; c++)
                {
                    var top = src[i00 + c] + ((src[i01 + c] - src[i00 + c]) * wx);
                    var bottom = src[i10 + c] + ((src[i11 + c] - src[i10 + c]) * wx);
                    dst[d + c] = (byte)Math.Clamp((int)MathF.Round(top + ((bottom - top) * wy)), 0, 255);
                }

                dst[d + 3] = 255;
            }
        });

        return dst;
    }

    private static void ApplyTone(byte[] pixels, int width, int height, ToneOps ops, CancellationToken cancellationToken)
    {
        var toLinear = ColourMath.ToLinear;
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var end = (y + 1) * width * 4;
            for (var i = y * width * 4; i < end; i += 4)
            {
                float b = toLinear[pixels[i]], g = toLinear[pixels[i + 1]], r = toLinear[pixels[i + 2]];
                ops.Apply(ref r, ref g, ref b);
                pixels[i] = ColourMath.ToByte(b);
                pixels[i + 1] = ColourMath.ToByte(g);
                pixels[i + 2] = ColourMath.ToByte(r);
            }
        });
    }
}
