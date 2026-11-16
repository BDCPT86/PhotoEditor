using FrameForge.Core.Editing;
using static FrameForge.Rendering.Tests.TestImages;

namespace FrameForge.Rendering.Tests;

public sealed class ToneTests
{
    private static (byte R, byte G, byte B) RenderPixel(byte r, byte g, byte b, EditRecipe recipe) =>
        Pixel(Render(Solid(4, 4, r, g, b), recipe), 1, 1);

    [Fact]
    public void NeutralRecipe_ReturnsIdenticalPixels()
    {
        var source = Gradient(37, 23);
        var output = Render(source, EditRecipe.Neutral);
        Assert.Equal(source.Bgra, output.Bgra);
        Assert.NotSame(source.Bgra, output.Bgra); // the source buffer is never shared or modified
    }

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(-1, 0.5)]
    [InlineData(2, 4.0)]
    public void Exposure_MultipliesLinearLight(double ev, double factor)
    {
        const byte grey = 100; // ≈ 0.127 linear, so ×4 stays below white
        var (r, g, b) = RenderPixel(grey, grey, grey, EditRecipe.Neutral with { Exposure = ev });

        Assert.Equal(Linear(grey) * factor, Linear(g), 2);
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void Exposure_ClipsAtWhite_NeverWraps()
    {
        var (r, g, b) = RenderPixel(240, 240, 240, EditRecipe.Neutral with { Exposure = 5 });
        Assert.Equal((255, 255, 255), (r, g, b));
    }

    [Fact]
    public void Temperature_WarmsAndCools_KeepingGreyLuminanceRoughly()
    {
        var warm = RenderPixel(128, 128, 128, EditRecipe.Neutral with { Temperature = 60 });
        var cool = RenderPixel(128, 128, 128, EditRecipe.Neutral with { Temperature = -60 });

        Assert.True(warm.R > warm.B);
        Assert.True(cool.B > cool.R);
        var luminance = (0.2126 * Linear(warm.R)) + (0.7152 * Linear(warm.G)) + (0.0722 * Linear(warm.B));
        Assert.Equal(Linear(128), luminance, 2);
    }

    [Fact]
    public void Tint_PositiveIsMagenta_NegativeIsGreen()
    {
        var magenta = RenderPixel(128, 128, 128, EditRecipe.Neutral with { Tint = 80 });
        var green = RenderPixel(128, 128, 128, EditRecipe.Neutral with { Tint = -80 });
        Assert.True(magenta.G < magenta.R && magenta.G < magenta.B);
        Assert.True(green.G > green.R && green.G > green.B);
    }

    [Fact]
    public void Contrast_SpreadsOrCompressesAroundMidTones()
    {
        byte dark = 50, light = 200;
        var more = EditRecipe.Neutral with { Contrast = 80 };
        var less = EditRecipe.Neutral with { Contrast = -80 };

        Assert.True(RenderPixel(dark, dark, dark, more).G < dark);
        Assert.True(RenderPixel(light, light, light, more).G > light);
        Assert.True(RenderPixel(dark, dark, dark, less).G > dark);
        Assert.True(RenderPixel(light, light, light, less).G < light);
    }

    [Fact]
    public void Shadows_LiftDarkTonesFarMoreThanBrightOnes()
    {
        var recipe = EditRecipe.Neutral with { Shadows = 100 };
        var darkGain = RenderPixel(30, 30, 30, recipe).G - 30;
        var brightGain = RenderPixel(230, 230, 230, recipe).G - 230;
        Assert.True(darkGain > 10, $"dark +{darkGain}");
        Assert.True(brightGain <= 1, $"bright +{brightGain}");
    }

    [Fact]
    public void Highlights_DarkenBrightTones_LeavingShadowsAlone()
    {
        var recipe = EditRecipe.Neutral with { Highlights = -100 };
        Assert.True(RenderPixel(230, 230, 230, recipe).G < 215);
        Assert.InRange(RenderPixel(30, 30, 30, recipe).G, 27, 30); // at most a couple of levels
    }

    [Theory]
    [InlineData(0, -100, 0)]
    [InlineData(0, 0, -100)]
    [InlineData(100, -100, -100)]
    [InlineData(-100, 100, 100)]
    public void ToneSliders_KeepBlackBlackAndWhiteWhite(double shadows, double highlights, double contrast)
    {
        // No grey haze: the end points never move, whatever the tone sliders do.
        var recipe = EditRecipe.Neutral with { Shadows = shadows, Highlights = highlights, Contrast = contrast };
        Assert.Equal((255, 255, 255), RenderPixel(255, 255, 255, recipe));
        Assert.Equal((0, 0, 0), RenderPixel(0, 0, 0, recipe));
    }

    [Fact]
    public void ToneCurve_IsMonotonic_ForExtremeSettings()
    {
        foreach (var c in new[] { -100.0, 0, 100 })
        {
            foreach (var h in new[] { -100.0, 0, 100 })
            {
                foreach (var s in new[] { -100.0, 0, 100 })
                {
                    var ops = new ToneOps(EditRecipe.Neutral with { Contrast = c, Highlights = h, Shadows = s });
                    var previous = -1f;
                    for (var p = 0f; p <= 1.5f; p += 0.001f)
                    {
                        var mapped = ops.Curve(p);
                        Assert.True(mapped >= previous - 1e-6f, $"c={c} h={h} s={s}: curve falls at p={p}");
                        previous = mapped;
                    }
                }
            }
        }
    }

    [Fact]
    public void Saturation_MinusHundred_MakesGrey_AndPlusIncreasesSpread()
    {
        var grey = RenderPixel(200, 80, 40, EditRecipe.Neutral with { Saturation = -100 });
        Assert.InRange(grey.R - grey.B, -2, 2);
        Assert.InRange(grey.G - grey.B, -2, 2);

        var vivid = RenderPixel(160, 110, 90, EditRecipe.Neutral with { Saturation = 50 });
        Assert.True(vivid.R - vivid.B > 160 - 90);
    }

    [Fact]
    public void Vibrance_BoostsMutedColoursMoreThanSaturatedOnes()
    {
        static int Spread((byte R, byte G, byte B) p) => Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B));
        var recipe = EditRecipe.Neutral with { Vibrance = 100 };
        var muted = (R: (byte)140, G: (byte)125, B: (byte)115);
        var strong = (R: (byte)230, G: (byte)40, B: (byte)30);

        var mutedGain = Spread(RenderPixel(muted.R, muted.G, muted.B, recipe)) / (double)Spread(muted);
        var strongGain = Spread(RenderPixel(strong.R, strong.G, strong.B, recipe)) / (double)Spread(strong);
        Assert.True(mutedGain > strongGain, $"muted ×{mutedGain:0.00} vs strong ×{strongGain:0.00}");
    }

    [Fact]
    public void Saturation_KeepsHue()
    {
        var p = RenderPixel(200, 120, 60, EditRecipe.Neutral with { Saturation = 40 });
        Assert.True(p.R > p.G && p.G > p.B); // orange stays orange
    }
}
