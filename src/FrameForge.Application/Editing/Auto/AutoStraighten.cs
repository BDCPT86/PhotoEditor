using System.Globalization;
using FrameForge.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Auto;

/// <summary>
/// Auto level: finds long near-horizontal or near-vertical straight lines (horizon, boundary rope, wall, posts) with a
/// Hough transform on edge pixels, and suggests the straighten angle that levels them. No model.
/// Options: the main line, a second distinct line when there is one, and "keep as is". A deliberately tilted shot
/// stays one click away.
/// </summary>
public static class AutoStraighten
{
    public const string Title = "Auto level";

    /// <summary>Lines are searched within this many degrees of horizontal or vertical.</summary>
    public const double SearchRange = 15;

    private const int WorkSize = 640;
    private const double AngleStep = 0.1;
    private const double MinLineShare = 0.25;    // a line must span a quarter of the frame
    private const double AlreadyLevel = 0.15;    // degrees
    private const double ContrastToBackground = 6; // peak votes vs the average voted bin

    internal sealed record Line(bool Horizontal, double Tilt, double Votes, double Share, double Rho, int Width, int Height);

    /// <param name="photo">The unedited, upright photo at any resolution.</param>
    public static AutoSuggestions Suggest(AnalysisImage photo, EditRecipe current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(current);

        // Work on the frame as the user sees it before straightening (quarter turns applied), small enough to be fast.
        var frame = EditRenderer.Render(photo, EditRecipe.Neutral with { QuarterTurns = current.QuarterTurns }, WorkSize, cancellationToken);
        var lines = FindLines(frame, cancellationToken);
        var culture = CultureInfo.CurrentCulture;

        if (lines.Count == 0)
        {
            return AutoSuggestions.None(Title, "No long straight line found to level against, so the angle is left as it is.");
        }

        var best = lines[0];
        if (Math.Abs(best.Tilt + current.Straighten) < AlreadyLevel)
        {
            return AutoSuggestions.None(Title, string.Create(culture, $"Already level: the main {Kind(best)} line is within {AlreadyLevel}°."));
        }

        var options = new List<AutoSuggestion> { Option("Main line", best, current, frame, culture) };
        if (lines.Skip(1).FirstOrDefault(l => Math.Abs(l.Tilt - best.Tilt) > 0.5 && l.Share >= best.Share * 0.4) is { } second)
        {
            options.Add(Option("Second line", second, current, frame, culture));
        }

        options.Add(new AutoSuggestion("Keep as is", current,
            string.Create(culture, $"Leave the angle at {current.Straighten:+0.0;-0.0;0.0}° (a tilted shot can be deliberate).")));
        return new AutoSuggestions(Title, options);
    }

    private static AutoSuggestion Option(string name, Line line, EditRecipe current, AnalysisImage frame, CultureInfo culture)
    {
        var angle = Math.Round(Math.Clamp(-line.Tilt, -EditRecipe.MaxStraighten, EditRecipe.MaxStraighten), 1);
        var recipe = current with { Straighten = angle };
        var reason = string.Create(culture,
            $"Levels the {(line.Horizontal ? "near-horizontal" : "near-vertical")} line spanning {line.Share:P0} of the frame (tilted {line.Tilt:+0.0;-0.0}°): straighten {angle:+0.0;-0.0;0.0}°.");
        return new AutoSuggestion(name, recipe.EnsureValid(), reason, Guide(line, recipe, frame));
    }

    /// <summary>The detected line's end points, mapped into the output of the suggested recipe.</summary>
    private static GuideLine? Guide(Line line, EditRecipe recipe, AnalysisImage frame)
    {
        var geometry = new EditGeometry(frame.Width, frame.Height, recipe with { QuarterTurns = 0 });
        var theta = (line.Horizontal ? 90 + line.Tilt : -line.Tilt) * Math.PI / 180;
        double cos = Math.Cos(theta), sin = Math.Sin(theta);

        // Points on x·cos + y·sin = ρ at the frame edges.
        (double X, double Y) a, b;
        if (line.Horizontal)
        {
            a = (0, line.Rho / sin);
            b = (frame.Width, (line.Rho - (frame.Width * cos)) / sin);
        }
        else
        {
            a = (line.Rho / cos, 0);
            b = ((line.Rho - (frame.Height * sin)) / cos, frame.Height);
        }

        var (u1, v1) = geometry.ToOutput(a.X, a.Y);
        var (u2, v2) = geometry.ToOutput(b.X, b.Y);
        return new GuideLine(u1, v1, u2, v2);
    }

    private static string Kind(Line line) => line.Horizontal ? "horizontal" : "vertical";

    /// <summary>Lines strongest first, each with its tilt in degrees (clockwise positive, image y pointing down).</summary>
    internal static List<Line> FindLines(AnalysisImage image, CancellationToken cancellationToken = default)
    {
        int w = image.Width, h = image.Height;
        var luma = image.ToLuminance();
        var edges = new List<(int X, int Y, double Gx, double Gy)>();
        var magnitudes = new List<double>();

        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                double L(int dx, int dy) => luma[((y + dy) * w) + x + dx];
                var gx = (L(1, -1) + (2 * L(1, 0)) + L(1, 1)) - (L(-1, -1) + (2 * L(-1, 0)) + L(-1, 1));
                var gy = (L(-1, 1) + (2 * L(0, 1)) + L(1, 1)) - (L(-1, -1) + (2 * L(0, -1)) + L(1, -1));
                var m = Math.Sqrt((gx * gx) + (gy * gy));
                if (m > 60)
                {
                    edges.Add((x, y, gx, gy));
                    magnitudes.Add(m);
                }
            }
        }

        if (edges.Count < Math.Min(w, h) / 2)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lines = new List<Line>();
        lines.AddRange(Hough(edges, w, h, horizontal: true));
        lines.AddRange(Hough(edges, w, h, horizontal: false));
        return lines.OrderByDescending(l => l.Share).ToList();
    }

    /// <summary>
    /// Hough transform over one family. Each edge pixel votes only for orientations close to its own gradient
    /// direction, so texture (grass, crowds) does not drown out real lines. Returns up to three separated peaks.
    /// </summary>
    private static List<Line> Hough(List<(int X, int Y, double Gx, double Gy)> edges, int w, int h, bool horizontal)
    {
        var steps = (int)Math.Round(2 * SearchRange / AngleStep) + 1;
        var diagonal = (int)Math.Ceiling(Math.Sqrt((w * w) + (h * h)));
        var rhoCount = (2 * diagonal) + 1;
        var votes = new float[steps * rhoCount];
        var cos = new double[steps];
        var sin = new double[steps];
        for (var t = 0; t < steps; t++)
        {
            var tilt = -SearchRange + (t * AngleStep);
            var theta = (horizontal ? 90 + tilt : -tilt) * Math.PI / 180; // the line's normal direction
            cos[t] = Math.Cos(theta);
            sin[t] = Math.Sin(theta);
        }

        foreach (var (x, y, gx, gy) in edges)
        {
            // The gradient is perpendicular to the edge; keep pixels whose edge runs along this family.
            var gradientAngle = Math.Atan2(gy, gx) * 180 / Math.PI;
            var normal = horizontal ? 90 : 0;
            var diff = Math.Abs(((((gradientAngle - normal) % 180) + 270) % 180) - 90);
            if (diff > SearchRange + 3)
            {
                continue;
            }

            for (var t = 0; t < steps; t++)
            {
                var rho = (int)Math.Round((x * cos[t]) + (y * sin[t])) + diagonal;
                votes[(t * rhoCount) + rho]++;
            }
        }

        // A real line stands far above the background: texture (grass, noise) spreads its votes evenly.
        double total = 0;
        var nonZero = 0;
        foreach (var v in votes)
        {
            if (v > 0)
            {
                total += v;
                nonZero++;
            }
        }

        var background = nonZero == 0 ? 0 : total / nonZero;
        var span = horizontal ? w : h;
        var found = new List<Line>();
        for (var peak = 0; peak < 3; peak++)
        {
            var bestIndex = 0;
            for (var i = 1; i < votes.Length; i++)
            {
                if (votes[i] > votes[bestIndex])
                {
                    bestIndex = i;
                }
            }

            var best = votes[bestIndex];
            if (best < span * MinLineShare || best < background * ContrastToBackground)
            {
                break;
            }

            int bt = bestIndex / rhoCount, br = bestIndex % rhoCount;

            // Neighbouring angles often tie (a short line fits several within a pixel): take the middle of the run of
            // near-equal peaks, not its first bin, so a level line reads as 0°, not −0.2°.
            int lo = bt, hi = bt;
            bool Near(int t) => Enumerable.Range(Math.Max(0, br - 1), 3).Any(r => r < rhoCount && votes[(t * rhoCount) + r] >= best * 0.99);
            while (lo > 0 && Near(lo - 1))
            {
                lo--;
            }

            while (hi < steps - 1 && Near(hi + 1))
            {
                hi++;
            }

            var tilt = -SearchRange + ((lo + hi) / 2.0 * AngleStep);
            found.Add(new Line(horizontal, Math.Round(tilt, 2), best, Math.Min(1, best / span), br - diagonal, w, h));

            // Suppress the neighbourhood so the next peak is a different line.
            for (var t = Math.Max(0, bt - 20); t < Math.Min(steps, bt + 21); t++)
            {
                for (var r = Math.Max(0, br - 15); r < Math.Min(rhoCount, br + 16); r++)
                {
                    votes[(t * rhoCount) + r] = 0;
                }
            }
        }

        return found;
    }
}
