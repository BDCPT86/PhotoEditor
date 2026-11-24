using System.Globalization;
using FrameForge.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// Select sky without a model: deterministic and explainable rules on the upright photo.
/// <list type="number">
/// <item>A pixel can be sky when it is fairly bright and either blue (blue above red) or a pale, almost neutral
/// grey or white (overcast), is not green, and its local brightness gradient is small (sky is smooth).</item>
/// <item>Sky grows from such pixels along the top edge (and from Include clicks), only between neighbours whose
/// brightness differs little, so it stops at roofs, trees and people. Sky-coloured pixels right at an edge are
/// included but do not grow further.</item>
/// <item>Small holes inside the sky (birds, wires, cloud detail) are filled. An Exclude click removes the connected
/// part of the sky it lands on.</item>
/// </list>
/// Known limits: orange or red sunset skies, skies that touch no top edge, and bright smooth walls touching the
/// top. Clicks can fix most of these.
/// </summary>
public sealed class SkyMaskGenerator : IMaskGenerator
{
    /// <summary>Bump when the rules change.</summary>
    public const int RulesVersion = 2;

    /// <summary>Minimum brightness (Rec. 709 luma, 0–1, gamma-encoded) for blue sky.</summary>
    internal const float MinBlueLuma = 0.30f;

    /// <summary>Minimum brightness for overcast sky, and its maximum spread between channels.</summary>
    internal const float MinGreyLuma = 0.55f;

    internal const float MaxGreySpread = 0.12f;

    /// <summary>Largest local gradient (luma per pixel, central difference) that still counts as smooth.</summary>
    internal const float MaxGradient = 0.06f;

    /// <summary>Largest brightness step between neighbours while growing.</summary>
    internal const float MaxStep = 0.035f;

    /// <summary>Top rows that seed the sky, as a share of the height.</summary>
    internal const double SeedRows = 0.02;

    /// <summary>Holes up to this share of the photo, enclosed by sky, are filled.</summary>
    internal const double MaxHole = 0.02;

    /// <summary>Less than this share of the photo is reported as no sky.</summary>
    internal const double MinCoverage = 0.005;

    /// <summary>
    /// Rules v2 (B7): a connected part of the sky is kept only when its median local texture (standard deviation of
    /// brightness in 5 × 5 pixels) is at most this. Real sky measured 0.0009–0.0058; walls, panels and brick 0.0076 or more.
    /// </summary>
    internal const float MaxPartTexture = 0.006f;

    /// <summary>Rules v2: ... and its mean brightness is at least this (shaded walls measured 0.32–0.49, sky 0.59 or more).</summary>
    internal const float MinPartLuma = 0.5f;

    public MaskKind Kind => MaskKind.Sky;

    public string Source => $"sky-rules@{RulesVersion}";

    public string? UnavailableReason => null;

    public MaskGeneration Generate(AnalysisImage image, IReadOnlyList<MaskPoint> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(points);
        int w = image.Width, h = image.Height, n = w * h;
        var (luma, candidate, smooth) = Classify(image, cancellationToken);
        var sky = new bool[n];
        var queue = new Queue<int>();
        void Seed(int i)
        {
            if (smooth[i] && !sky[i])
            {
                sky[i] = true;
                queue.Enqueue(i);
            }
        }

        var seedRows = Math.Max(1, (int)Math.Ceiling(h * SeedRows));
        for (var y = 0; y < seedRows; y++)
        {
            for (var x = 0; x < w; x++)
            {
                Seed((y * w) + x);
            }
        }

        foreach (var point in points.Where(p => p.Include))
        {
            Seed(IndexOf(point, w, h));
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            Grow(i, x > 0 ? i - 1 : -1);
            Grow(i, x < w - 1 ? i + 1 : -1);
            Grow(i, y > 0 ? i - w : -1);
            Grow(i, y < h - 1 ? i + w : -1);
        }

        void Grow(int from, int to)
        {
            if (to >= 0 && !sky[to] && candidate[to] && Math.Abs(luma[to] - luma[from]) <= MaxStep)
            {
                // Sky-coloured pixels at an edge (the horizon, a roof line) join the sky, but it grows on only from smooth ones.
                sky[to] = true;
                if (smooth[to])
                {
                    queue.Enqueue(to);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var clicked = points.Where(p => p.Include).Select(p => IndexOf(p, w, h)).ToList();
        KeepSkyLikeParts(sky, w, h, luma, LocalTexture(luma, w, h, 2), clicked);
        FillHoles(sky, w, h, (int)(n * MaxHole));
        foreach (var point in points.Where(p => !p.Include))
        {
            RemoveComponent(sky, w, h, IndexOf(point, w, h));
        }

        var alpha = new byte[n];
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            if (sky[i])
            {
                alpha[i] = 255;
                count++;
            }
        }

        var coverage = count / (double)n;
        return coverage < MinCoverage
            ? MaskGeneration.NotFound("No sky found. Click the sky to select it.")
            : MaskGeneration.Found(new MaskImage(w, h, alpha), string.Create(CultureInfo.InvariantCulture, $"Sky covers {coverage * 100:0}% of the photo"));
    }

    /// <summary>Per pixel: brightness, whether it has a sky colour, and whether sky can grow on from it (sky colour and smooth).</summary>
    internal static (float[] Luma, bool[] Candidate, bool[] Smooth) Classify(AnalysisImage image, CancellationToken cancellationToken)
    {
        int w = image.Width, h = image.Height, n = w * h;
        var luma = new float[n];
        var candidate = new bool[n]; // sky colour
        var smooth = new bool[n];    // sky colour and a small gradient: sky can grow on from here
        var px = image.Bgra;
        for (int i = 0, p = 0; i < n; i++, p += 4)
        {
            float b = px[p] / 255f, g = px[p + 1] / 255f, r = px[p + 2] / 255f;
            var y = (0.0722f * b) + (0.7152f * g) + (0.2126f * r);
            luma[i] = y;
            var spread = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            var green = g > b + 0.02f && g > r;
            var blue = b > r + 0.03f && y >= MinBlueLuma;
            var grey = y >= MinGreyLuma && spread <= MaxGreySpread;
            candidate[i] = !green && (blue || grey);
        }

        cancellationToken.ThrowIfCancellationRequested();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w) + x;
                if (!candidate[i])
                {
                    continue;
                }

                var gx = (luma[(y * w) + Math.Min(x + 1, w - 1)] - luma[(y * w) + Math.Max(x - 1, 0)]) / 2;
                var gy = (luma[(Math.Min(y + 1, h - 1) * w) + x] - luma[(Math.Max(y - 1, 0) * w) + x]) / 2;
                smooth[i] = MathF.Sqrt((gx * gx) + (gy * gy)) <= MaxGradient;
            }
        }

        return (luma, candidate, smooth);
    }

    private static int IndexOf(MaskPoint point, int w, int h) =>
        (Math.Clamp((int)(point.Y * h), 0, h - 1) * w) + Math.Clamp((int)(point.X * w), 0, w - 1);

    /// <summary>
    /// Clears each connected part of <paramref name="sky"/> that is textured (median local texture above
    /// <see cref="MaxPartTexture"/>) or dark (mean brightness below <see cref="MinPartLuma"/>), such as a painted wall or a
    /// shaded panel touching the top edge. A part containing an Include click is always kept.
    /// </summary>
    internal static void KeepSkyLikeParts(bool[] sky, int w, int h, float[] luma, float[] texture, IReadOnlyCollection<int> clicked)
    {
        var seen = new bool[sky.Length];
        var part = new List<int>();
        var stack = new Stack<int>();
        var textures = new List<float>();
        for (var start = 0; start < sky.Length; start++)
        {
            if (!sky[start] || seen[start])
            {
                continue;
            }

            part.Clear();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                part.Add(i);
                foreach (var j in Neighbours(i, i % w, i / w, w, h))
                {
                    if (sky[j] && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (clicked.Any(part.Contains))
            {
                continue;
            }

            textures.Clear();
            double lumaSum = 0;
            foreach (var i in part)
            {
                textures.Add(texture[i]);
                lumaSum += luma[i];
            }

            textures.Sort();
            if (textures[textures.Count / 2] > MaxPartTexture || lumaSum / part.Count < MinPartLuma)
            {
                foreach (var i in part)
                {
                    sky[i] = false;
                }
            }
        }
    }

    /// <summary>Standard deviation of brightness in a (2r+1)² window around each pixel (summed-area tables).</summary>
    internal static float[] LocalTexture(float[] luma, int w, int h, int r)
    {
        var sum = new double[(w + 1) * (h + 1)];
        var sq = new double[(w + 1) * (h + 1)];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                double v = luma[(y * w) + x];
                var k = ((y + 1) * (w + 1)) + x + 1;
                sum[k] = v + sum[k - 1] + sum[k - w - 1] - sum[k - w - 2];
                sq[k] = (v * v) + sq[k - 1] + sq[k - w - 1] - sq[k - w - 2];
            }
        }

        var result = new float[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int xa = Math.Max(0, x - r), xb = Math.Min(w, x + r + 1), ya = Math.Max(0, y - r), yb = Math.Min(h, y + r + 1);
                var n = (xb - xa) * (yb - ya);
                var s = sum[(yb * (w + 1)) + xb] - sum[(ya * (w + 1)) + xb] - sum[(yb * (w + 1)) + xa] + sum[(ya * (w + 1)) + xa];
                var q = sq[(yb * (w + 1)) + xb] - sq[(ya * (w + 1)) + xb] - sq[(yb * (w + 1)) + xa] + sq[(ya * (w + 1)) + xa];
                result[(y * w) + x] = (float)Math.Sqrt(Math.Max(0, (q / n) - ((s / n) * (s / n))));
            }
        }

        return result;
    }

    /// <summary>Marks as sky every non-sky region that does not touch the border and is at most <paramref name="maxSize"/> pixels.</summary>
    internal static void FillHoles(bool[] sky, int w, int h, int maxSize)
    {
        var seen = new bool[sky.Length];
        var region = new List<int>();
        var stack = new Stack<int>();
        for (var start = 0; start < sky.Length; start++)
        {
            if (sky[start] || seen[start])
            {
                continue;
            }

            region.Clear();
            var touchesBorder = false;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                region.Add(i);
                int x = i % w, y = i / w;
                touchesBorder |= x == 0 || y == 0 || x == w - 1 || y == h - 1;
                foreach (var j in Neighbours(i, x, y, w, h))
                {
                    if (!sky[j] && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (!touchesBorder && region.Count <= maxSize)
            {
                foreach (var i in region)
                {
                    sky[i] = true;
                }
            }
        }
    }

    /// <summary>Clears the connected sky region containing <paramref name="start"/> (nothing if it is not sky).</summary>
    internal static void RemoveComponent(bool[] sky, int w, int h, int start)
    {
        if (!sky[start])
        {
            return;
        }

        var stack = new Stack<int>();
        sky[start] = false;
        stack.Push(start);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            foreach (var j in Neighbours(i, i % w, i / w, w, h))
            {
                if (sky[j])
                {
                    sky[j] = false;
                    stack.Push(j);
                }
            }
        }
    }

    private static IEnumerable<int> Neighbours(int i, int x, int y, int w, int h)
    {
        if (x > 0)
        {
            yield return i - 1;
        }

        if (x < w - 1)
        {
            yield return i + 1;
        }

        if (y > 0)
        {
            yield return i - w;
        }

        if (y < h - 1)
        {
            yield return i + w;
        }
    }
}
