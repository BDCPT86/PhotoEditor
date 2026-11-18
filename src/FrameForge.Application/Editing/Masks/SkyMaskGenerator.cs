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
    public const int RulesVersion = 1;

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

    public MaskKind Kind => MaskKind.Sky;

    public string Source => $"sky-rules@{RulesVersion}";

    public string? UnavailableReason => null;

    public MaskGeneration Generate(AnalysisImage image, IReadOnlyList<MaskPoint> points, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(points);
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

    private static int IndexOf(MaskPoint point, int w, int h) =>
        (Math.Clamp((int)(point.Y * h), 0, h - 1) * w) + Math.Clamp((int)(point.X * w), 0, w - 1);

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
