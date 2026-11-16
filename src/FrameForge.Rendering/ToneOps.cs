using FrameForge.Core.Editing;

namespace FrameForge.Rendering;

/// <summary>
/// Per-pixel tone and colour for one recipe, with constants precomputed. All operations work on linear-light RGB.
/// Order: white balance and exposure (channel gains) → highlights/shadows/contrast (a curve on luminance, applied as a
/// gain so hue is kept) → saturation/vibrance (OKLab chroma). Every curve is monotonic, so tones never swap order.
/// </summary>
internal sealed class ToneOps
{
    private const float Gamma = 2.2f;

    /// <summary>Normalises p·(1−p)³ and p³·(1−p) to a peak of 1.</summary>
    private const float BumpScale = 256f / 27;

    /// <summary>Strength when moving a bump away from its steep side (monotonic up to 1/2.37 ≈ 0.42).</summary>
    private const float StrongSide = 0.35f;

    /// <summary>Strength when moving against its steep side (monotonic up to 1/9.48 ≈ 0.105).</summary>
    private const float WeakSide = 0.10f;

    /// <summary>OKLab chroma treated as "fully saturated" by vibrance (most real-world colours are below this).</summary>
    private const float VibranceChromaRange = 0.25f;

    private readonly float _gainR;
    private readonly float _gainG;
    private readonly float _gainB;
    private readonly bool _hasCurve;
    private readonly float _shadows;
    private readonly float _highlights;
    private readonly float _contrast;
    private readonly bool _hasColour;
    private readonly float _saturation;
    private readonly float _vibrance;

    public ToneOps(EditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var (r, g, b) = WhiteBalanceGains(recipe.Temperature, recipe.Tint);
        var exposure = (float)Math.Pow(2, recipe.Exposure);
        (_gainR, _gainG, _gainB) = (r * exposure, g * exposure, b * exposure);

        _shadows = (float)(recipe.Shadows / EditRecipe.MaxSlider);
        _highlights = (float)(recipe.Highlights / EditRecipe.MaxSlider);
        _contrast = (float)(recipe.Contrast / EditRecipe.MaxSlider);
        _hasCurve = _shadows != 0 || _highlights != 0 || _contrast != 0;

        _saturation = (float)(recipe.Saturation / EditRecipe.MaxSlider);
        _vibrance = (float)(recipe.Vibrance / EditRecipe.MaxSlider);
        _hasColour = _saturation != 0 || _vibrance != 0;
    }

    /// <summary>
    /// Channel gains for a relative white balance shift, normalised so a neutral grey keeps its luminance.
    /// Temperature moves along blue↔amber (±1 stop on red/blue at the ends), tint along green↔magenta.
    /// </summary>
    public static (float R, float G, float B) WhiteBalanceGains(double temperature, double tint)
    {
        var t = temperature / EditRecipe.MaxSlider;
        var n = tint / EditRecipe.MaxSlider;
        double r = Math.Pow(2, 0.5 * t), g = Math.Pow(2, -0.3 * n), b = Math.Pow(2, -0.5 * t);
        var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        return ((float)(r / luminance), (float)(g / luminance), (float)(b / luminance));
    }

    /// <summary>
    /// The tone curve on perceptual lightness p = Y^(1/2.2): shadows, then highlights, then contrast. Every step keeps
    /// black at 0 and white at 1, so nothing turns into a grey haze, and is monotonic so tones never swap order.
    /// Shadows and highlights add a bump that peaks at p = ¼ and p = ¾:
    /// shadows B(p) = (256/27)·p·(1−p)³, highlights B(p) = (256/27)·p³·(1−p), each with a peak of 1. Their slopes range
    /// over [−2.37, 9.48], so the strength is limited separately for each direction (lifting a bump is safe up to
    /// 1/2.37, pushing against its steep side only up to 1/9.48). Values above white (from +exposure) are left alone
    /// and clip when encoded.
    /// </summary>
    public float Curve(float p)
    {
        if (p >= 1)
        {
            return p;
        }

        if (_shadows != 0)
        {
            var bump = BumpScale * p * (1 - p) * (1 - p) * (1 - p);
            p += _shadows * (_shadows > 0 ? StrongSide : WeakSide) * bump;
        }

        if (_highlights != 0)
        {
            var bump = BumpScale * p * p * p * (1 - p);
            p += _highlights * (_highlights > 0 ? WeakSide : StrongSide) * bump;
        }

        if (_contrast > 0)
        {
            p += _contrast * (SmoothStep(0f, 1f, p) - p);
        }
        else if (_contrast < 0)
        {
            // Inverse smoothstep: flattens mid-tones while keeping the end points.
            var inverse = 0.5f - MathF.Sin(MathF.Asin(Math.Clamp(1 - (2 * p), -1f, 1f)) / 3);
            p += -_contrast * (inverse - p);
        }

        return Math.Clamp(p, 0, 1);
    }

    /// <summary>Applies the recipe to one pixel in place (linear light, may exceed 1 until encoded).</summary>
    public void Apply(ref float r, ref float g, ref float b)
    {
        r *= _gainR;
        g *= _gainG;
        b *= _gainB;

        if (_hasCurve)
        {
            var y = (0.2126f * r) + (0.7152f * g) + (0.0722f * b);
            if (y > 1e-7f)
            {
                var mapped = MathF.Pow(Curve(MathF.Pow(y, 1 / Gamma)), Gamma);
                var gain = mapped / y;
                r *= gain;
                g *= gain;
                b *= gain;
            }
        }

        if (_hasColour)
        {
            var (l, a, bb) = ColourMath.LinearToOkLab(MathF.Max(r, 0), MathF.Max(g, 0), MathF.Max(b, 0));
            var chroma = MathF.Sqrt((a * a) + (bb * bb));
            var factor = (1 + _saturation) * (1 + (_vibrance * (1 - MathF.Min(chroma / VibranceChromaRange, 1))));
            factor = MathF.Max(factor, 0);
            (r, g, b) = ColourMath.OkLabToLinear(l, a * factor, bb * factor);
        }
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - (2 * t));
    }
}
