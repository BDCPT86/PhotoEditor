namespace FrameForge.Rendering;

/// <summary>sRGB transfer functions and OKLab conversion (Björn Ottosson, 2020; public-domain formulas).</summary>
internal static class ColourMath
{
    /// <summary>Encoding table indexed by √linear, which keeps shadows precise without a pow per pixel.</summary>
    private const int EncodeSize = 8192;

    /// <summary>sRGB byte → linear light.</summary>
    public static readonly float[] ToLinear = BuildToLinear();

    private static readonly byte[] Encode = BuildEncode();

    public static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    public static double LinearToSrgb(double v) => v <= 0.0031308 ? v * 12.92 : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    /// <summary>Linear light (clamped to 0–1) → sRGB byte.</summary>
    public static byte ToByte(float linear)
    {
        if (!(linear > 0))
        {
            return 0;
        }

        if (linear >= 1)
        {
            return 255;
        }

        return Encode[(int)((MathF.Sqrt(linear) * (EncodeSize - 1)) + 0.5f)];
    }

    public static (float L, float A, float B) LinearToOkLab(float r, float g, float b)
    {
        var l = MathF.Cbrt((0.4122214708f * r) + (0.5363325363f * g) + (0.0514459929f * b));
        var m = MathF.Cbrt((0.2119034982f * r) + (0.6806995451f * g) + (0.1073969566f * b));
        var s = MathF.Cbrt((0.0883024619f * r) + (0.2817188376f * g) + (0.6299787005f * b));
        return (
            (0.2104542553f * l) + (0.7936177850f * m) - (0.0040720468f * s),
            (1.9779984951f * l) - (2.4285922050f * m) + (0.4505937099f * s),
            (0.0259040371f * l) + (0.7827717662f * m) - (0.8086757660f * s));
    }

    public static (float R, float G, float B) OkLabToLinear(float lightness, float a, float b)
    {
        var l = lightness + (0.3963377774f * a) + (0.2158037573f * b);
        var m = lightness - (0.1055613458f * a) - (0.0638541728f * b);
        var s = lightness - (0.0894841775f * a) - (1.2914855480f * b);
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        return (
            (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s),
            (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s),
            (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s));
    }

    private static float[] BuildToLinear()
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (float)SrgbToLinear(i / 255.0);
        }

        return table;
    }

    private static byte[] BuildEncode()
    {
        var table = new byte[EncodeSize];
        for (var i = 0; i < EncodeSize; i++)
        {
            var root = i / (double)(EncodeSize - 1);
            table[i] = (byte)Math.Clamp((int)Math.Round(LinearToSrgb(root * root) * 255), 0, 255);
        }

        return table;
    }
}
