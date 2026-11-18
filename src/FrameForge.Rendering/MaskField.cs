using FrameForge.Analysis;

namespace FrameForge.Rendering;

/// <summary>
/// A mask prepared for one render: mapped through the recipe's geometry (so it follows quarter turns, straighten and
/// crop), optionally inverted, and refined with a guided filter (He, Sun and Tang, 2010/2013) so its edge follows the
/// photo's own edges instead of the mask's coarse outline, which avoids halos.
/// <para>
/// The filter runs on a fixed working grid (<see cref="WorkLongSide"/> on the long side of the edited frame, whatever
/// the output size), with the guide box-filtered from the source to about that density. Its linear coefficients are
/// then sampled at each output pixel and applied to that pixel's own brightness (the "fast guided filter"), so the edge
/// is as sharp as the output while preview and export share one mask field.
/// </para>
/// </summary>
internal sealed class MaskField
{
    /// <summary>Long side of the working grid. Masks are computed at 1024 px, so more would add no detail.</summary>
    public const int WorkLongSide = 1024;

    /// <summary>Filter radius in working pixels (about one cell of MobileSAM's 256 × 256 mask).</summary>
    public const int Radius = 6;

    /// <summary>Regularisation on brightness 0–1: edges weaker than about √ε ≈ 0.03 are smoothed over.</summary>
    public const float Epsilon = 1e-3f;

    private readonly int _width;
    private readonly int _height;
    private readonly float[] _a;
    private readonly float[] _b;

    private MaskField(int width, int height, float[] a, float[] b)
    {
        _width = width;
        _height = height;
        _a = a;
        _b = b;
    }

    public static MaskField Build(AnalysisImage source, EditGeometry geometry, MaskImage mask, bool invert, CancellationToken cancellationToken)
    {
        var scale = WorkLongSide / Math.Max(geometry.OutputWidth, geometry.OutputHeight);
        var w = Math.Max(1, (int)Math.Round(geometry.OutputWidth * scale));
        var h = Math.Max(1, (int)Math.Round(geometry.OutputHeight * scale));

        // Box-filter the source's brightness to roughly the working density, so a full-size export and a smaller
        // preview give nearly the same guide (point-sampling a large image would alias).
        var factor = Math.Max(1, (int)(Math.Max(geometry.OutputWidth, geometry.OutputHeight) / WorkLongSide));
        var (guideSource, gw, gh) = Downscaled(source, factor);

        var guide = new float[w * h];
        var p = new float[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var v = (y + 0.5) / h;
            for (var x = 0; x < w; x++)
            {
                var (sx, sy) = geometry.ToSource((x + 0.5) / w, v);
                var i = (y * w) + x;
                guide[i] = Bilinear(guideSource, gw, gh, (sx / factor) - 0.5, (sy / factor) - 0.5);
                var m = SampleMask(mask, sx / source.Width, sy / source.Height);
                p[i] = invert ? 1 - m : m;
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
        var (a, b) = GuidedCoefficients(guide, p, w, h, Radius, Epsilon);
        return new MaskField(w, h, a, b);
    }

    /// <summary>Mask strength 0–1 at normalised output position (u, v) for an output pixel of brightness <paramref name="luma"/> (0–1).</summary>
    public float Alpha(double u, double v, float luma)
    {
        var fx = Math.Clamp((u * _width) - 0.5, 0, _width - 1);
        var fy = Math.Clamp((v * _height) - 0.5, 0, _height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, _width - 1), y1 = Math.Min(y0 + 1, _height - 1);
        float wx = (float)(fx - x0), wy = (float)(fy - y0);
        float Lerp(float[] m)
        {
            var top = m[(y0 * _width) + x0] + ((m[(y0 * _width) + x1] - m[(y0 * _width) + x0]) * wx);
            var bottom = m[(y1 * _width) + x0] + ((m[(y1 * _width) + x1] - m[(y1 * _width) + x0]) * wx);
            return top + ((bottom - top) * wy);
        }

        return Math.Clamp((Lerp(_a) * luma) + Lerp(_b), 0f, 1f);
    }

    /// <summary>Gamma-encoded Rec. 709 brightness of a BGRA pixel, 0–1 (the guide).</summary>
    public static float Luma(byte[] bgra, int i) => ((0.0722f * bgra[i]) + (0.7152f * bgra[i + 1]) + (0.2126f * bgra[i + 2])) / 255f;

    /// <summary>
    /// Guided filter coefficients: in each (2r+1)² window, q = a·I + b fits the mask p as closely as possible with
    /// a = cov(I, p) / (var(I) + ε) and b = mean(p) − a·mean(I); a and b are then averaged over the windows.
    /// </summary>
    internal static (float[] A, float[] B) GuidedCoefficients(float[] guide, float[] p, int w, int h, int r, float eps)
    {
        var n = w * h;
        var ip = new float[n];
        var ii = new float[n];
        for (var i = 0; i < n; i++)
        {
            ip[i] = guide[i] * p[i];
            ii[i] = guide[i] * guide[i];
        }

        var meanI = BoxMean(guide, w, h, r);
        var meanP = BoxMean(p, w, h, r);
        var meanIp = BoxMean(ip, w, h, r);
        var meanIi = BoxMean(ii, w, h, r);
        var a = new float[n];
        var b = new float[n];
        for (var i = 0; i < n; i++)
        {
            var variance = meanIi[i] - (meanI[i] * meanI[i]);
            var covariance = meanIp[i] - (meanI[i] * meanP[i]);
            a[i] = covariance / (variance + eps);
            b[i] = meanP[i] - (a[i] * meanI[i]);
        }

        return (BoxMean(a, w, h, r), BoxMean(b, w, h, r));
    }

    /// <summary>Mean over a (2r+1)² window, clipped at the borders (separable running sums, O(n)).</summary>
    internal static float[] BoxMean(float[] values, int w, int h, int r)
    {
        var rows = new float[values.Length];
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            double sum = 0;
            for (var x = 0; x <= Math.Min(r, w - 1); x++)
            {
                sum += values[row + x];
            }

            for (var x = 0; x < w; x++)
            {
                int left = Math.Max(0, x - r), right = Math.Min(w - 1, x + r);
                rows[row + x] = (float)(sum / (right - left + 1));
                if (x + r + 1 < w)
                {
                    sum += values[row + x + r + 1];
                }

                if (x - r >= 0)
                {
                    sum -= values[row + x - r];
                }
            }
        }

        var result = new float[values.Length];
        for (var x = 0; x < w; x++)
        {
            double sum = 0;
            for (var y = 0; y <= Math.Min(r, h - 1); y++)
            {
                sum += rows[(y * w) + x];
            }

            for (var y = 0; y < h; y++)
            {
                int top = Math.Max(0, y - r), bottom = Math.Min(h - 1, y + r);
                result[(y * w) + x] = (float)(sum / (bottom - top + 1));
                if (y + r + 1 < h)
                {
                    sum += rows[((y + r + 1) * w) + x];
                }

                if (y - r >= 0)
                {
                    sum -= rows[((y - r) * w) + x];
                }
            }
        }

        return result;
    }

    private static (float[] Luma, int Width, int Height) Downscaled(AnalysisImage source, int factor)
    {
        int w = Math.Max(1, source.Width / factor), h = Math.Max(1, source.Height / factor);
        var luma = new float[w * h];
        var px = source.Bgra;
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                float sum = 0;
                var count = 0;
                for (var dy = 0; dy < factor && (y * factor) + dy < source.Height; dy++)
                {
                    for (var dx = 0; dx < factor && (x * factor) + dx < source.Width; dx++)
                    {
                        sum += Luma(px, ((((y * factor) + dy) * source.Width) + (x * factor) + dx) * 4);
                        count++;
                    }
                }

                luma[(y * w) + x] = sum / count;
            }
        });

        return (luma, w, h);
    }

    /// <summary>The mask at normalised source position (nx, ny), 0–1; bilinear, clamped at the edges.</summary>
    private static float SampleMask(MaskImage mask, double nx, double ny)
    {
        var fx = Math.Clamp((nx * mask.Width) - 0.5, 0, mask.Width - 1);
        var fy = Math.Clamp((ny * mask.Height) - 0.5, 0, mask.Height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, mask.Width - 1), y1 = Math.Min(y0 + 1, mask.Height - 1);
        float wx = (float)(fx - x0), wy = (float)(fy - y0);
        var m = mask.Alpha;
        var top = m[(y0 * mask.Width) + x0] + ((m[(y0 * mask.Width) + x1] - m[(y0 * mask.Width) + x0]) * wx);
        var bottom = m[(y1 * mask.Width) + x0] + ((m[(y1 * mask.Width) + x1] - m[(y1 * mask.Width) + x0]) * wx);
        return (top + ((bottom - top) * wy)) / 255f;
    }

    private static float Bilinear(float[] map, int w, int h, double fx, double fy)
    {
        fx = Math.Clamp(fx, 0, w - 1);
        fy = Math.Clamp(fy, 0, h - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float wx = (float)(fx - x0), wy = (float)(fy - y0);
        var top = map[(y0 * w) + x0] + ((map[(y0 * w) + x1] - map[(y0 * w) + x0]) * wx);
        var bottom = map[(y1 * w) + x0] + ((map[(y1 * w) + x1] - map[(y1 * w) + x0]) * wx);
        return top + ((bottom - top) * wy);
    }
}
