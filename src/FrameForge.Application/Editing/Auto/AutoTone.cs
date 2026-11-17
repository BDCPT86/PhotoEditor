using System.Globalization;
using FrameForge.Analysis;
using FrameForge.Core.Editing;

namespace FrameForge.Application.Editing.Auto;

/// <summary>
/// Auto light: suggests exposure, white balance and tone from the photo's own statistics. No model.
/// Statistics come from the unedited photo, so the values are absolute (they replace the tone settings) and the same
/// photo always gets the same suggestions. Options: Natural, Brighter, Punchy.
/// </summary>
public static class AutoTone
{
    public const string Title = "Auto light";

    /// <summary>Linear mid-tone target for Natural (≈ 44 % perceptual lightness, a typical exposed mid-grey).</summary>
    private const double NaturalTarget = 0.16;
    /// <summary>Brighter is always this much above Natural (if the highlights allow), so the two never coincide.</summary>
    private const double BrighterStep = 0.4;
    private const double MaxExposureStep = 1.5;
    private const int MaxSamples = 250_000;
    /// <summary>Smallest temperature correction worth suggesting; weaker casts are left as shot.</summary>
    private const double MinTemperature = 25;
    /// <summary>Clipped share below which highlights are left alone.</summary>
    private const double MinClippedShare = 0.005;

    internal sealed record Stats(double MedianLinear, double P99Linear, double ClippedShare, double DarkShare, double? NeutralBlueRedLog2, double NeutralShare);

    public static AutoSuggestions Suggest(AnalysisImage photo, EditRecipe current)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(current);
        var s = Measure(photo);
        var culture = CultureInfo.CurrentCulture;

        var temperature = Temperature(s.NeutralBlueRedLog2);
        var highlights = Highlights(s.ClippedShare);
        var shadows = s.DarkShare > 0.1 ? Math.Round(Math.Min(50, s.DarkShare * 150)) : 0;

        AutoSuggestion Make(string name, double extraEv, double contrast, double vibrance, double extraHighlights, double extraShadows)
        {
            var (ev, limited) = Exposure(s, NaturalTarget, extraEv);
            var recipe = current with
            {
                Exposure = ev,
                Temperature = temperature,
                Tint = 0,
                Contrast = contrast,
                Highlights = Math.Clamp(highlights + extraHighlights, -100, 0),
                Shadows = Math.Clamp(shadows + extraShadows, 0, 100),
                Vibrance = vibrance,
            };

            var parts = new List<string>
            {
                ev == 0 && Perceptual(s.MedianLinear) > Perceptual(NaturalTarget)
                    ? string.Create(culture, $"bright scene (mid-tones at {Perceptual(s.MedianLinear):P0}), exposure left as shot")
                    : string.Create(culture, $"mid-tones at {Perceptual(s.MedianLinear):P0} → {ev:+0.00;-0.00;0.00} EV")
                        + (limited ? " (held back to protect highlights)" : string.Empty),
            };
            if (highlights < 0)
            {
                parts.Add(string.Create(culture, $"{s.ClippedShare:P1} already clipped, highlights {recipe.Highlights:+0;-0;0}"));
            }

            if (recipe.Shadows > 0)
            {
                parts.Add(string.Create(culture, $"{s.DarkShare:P0} deep shadow, shadows {recipe.Shadows:+0}"));
            }

            parts.Add(temperature == 0
                ? (s.NeutralBlueRedLog2 is null ? "too few neutral areas to judge white balance" : "white balance left as shot (no strong cast)")
                : string.Create(culture, $"neutral areas lean {(temperature < 0 ? "warm" : "cool")}, temperature {temperature:+0;-0}"));
            if (contrast != 0 || vibrance != 0)
            {
                parts.Add(string.Create(culture, $"contrast {contrast:+0}, vibrance {vibrance:+0}"));
            }

            return new AutoSuggestion(name, recipe.EnsureValid(), Capitalise(string.Join("; ", parts)) + ".");
        }

        return new AutoSuggestions(Title,
        [
            Make("Natural", 0, 0, 0, 0, 0),
            Make("Brighter", BrighterStep, 0, 0, -10, 10),
            Make("Punchy", 0, 25, 20, -15, 0),
        ]);
    }

    /// <summary>
    /// White balance only for a strong cast. In the blind A/B (docs/evaluation-m08.md) the user never preferred the
    /// mild warming grey-world suggested (+2 to +24), so anything below <see cref="MinTemperature"/> is left as shot.
    /// </summary>
    internal static double Temperature(double? neutralBlueRedLog2)
    {
        if (neutralBlueRedLog2 is not { } log)
        {
            return 0;
        }

        var estimate = 100 * log * 0.6; // 60 % strength: grey-world on a few neutral pixels is a hint, not truth
        return Math.Abs(estimate) < MinTemperature ? 0 : Math.Clamp(Math.Round(estimate), -30, 30);
    }

    /// <summary>
    /// Highlights only when a real share of the photo is clipped. In the blind A/B small pull-downs on barely clipped
    /// photos made them look duller and lost, so below <see cref="MinClippedShare"/> nothing changes.
    /// </summary>
    internal static double Highlights(double clippedShare) =>
        clippedShare < MinClippedShare ? 0 : -Math.Round(Math.Min(40, clippedShare * 100 * 8));

    /// <summary>
    /// Exposure that brings the median towards the target. Brightening is held back so unclipped highlights stay
    /// below white. Auto never darkens: a bright scene (sun, white kit) is usually exposed correctly, darkening
    /// already-clipped whites only turns them grey, and in the blind A/B every darkened photo lost to the original.
    /// Clipped highlights are left to the Highlights slider.
    /// </summary>
    internal static (double Ev, bool Limited) Exposure(Stats s, double target, double extra = 0)
    {
        var ev = Math.Max(0, Math.Log2(target / Math.Max(s.MedianLinear, 1e-4)));

        ev += extra;
        var limited = false;
        if (ev > 0 && s.ClippedShare < 0.005 && s.P99Linear > 0)
        {
            var headroom = Math.Log2(0.95 / s.P99Linear);
            if (ev > headroom)
            {
                ev = Math.Max(0, headroom);
                limited = true;
            }
        }

        ev = Math.Clamp(ev, -MaxExposureStep, MaxExposureStep);
        return (Math.Round(ev * 20) / 20, limited);
    }

    internal static Stats Measure(AnalysisImage photo)
    {
        var px = photo.Bgra;
        var count = photo.Width * photo.Height;
        var step = Math.Max(1, count / MaxSamples);
        var luminance = new List<double>(count / step + 1);
        var brightest = new List<double>(count / step + 1);
        int clipped = 0, dark = 0, neutral = 0, samples = 0;
        double neutralR = 0, neutralB = 0;

        for (var i = 0; i < count; i += step)
        {
            var p = i * 4;
            byte b = px[p], g = px[p + 1], r = px[p + 2];
            double lr = Linear(r), lg = Linear(g), lb = Linear(b);
            var y = (0.2126 * lr) + (0.7152 * lg) + (0.0722 * lb);
            luminance.Add(y);
            brightest.Add(Math.Max(lr, Math.Max(lg, lb)));
            samples++;
            if (Math.Max(r, Math.Max(g, b)) >= 250)
            {
                clipped++;
                continue; // clipped pixels say nothing about white balance
            }

            var perceptual = Perceptual(y);
            if (perceptual < 0.12)
            {
                dark++;
                continue;
            }

            // Near-neutral mid-tones (white kit, walls, cloud, concrete) estimate the colour cast.
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (perceptual < 0.92 && max > 0 && (max - min) / (double)max < 0.18)
            {
                neutral++;
                neutralR += lr;
                neutralB += lb;
            }
        }

        luminance.Sort();
        brightest.Sort();
        var neutralShare = neutral / (double)samples;
        double? cast = neutralShare >= 0.02 && neutralR > 0 && neutralB > 0 ? Math.Log2(neutralB / neutralR) : null;
        return new Stats(
            luminance[luminance.Count / 2],
            brightest[(int)(brightest.Count * 0.99)],
            clipped / (double)samples,
            dark / (double)samples,
            cast,
            neutralShare);
    }

    private static double Linear(byte v)
    {
        var c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Perceptual(double linear) => Math.Pow(Math.Max(linear, 0), 1 / 2.2);

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
