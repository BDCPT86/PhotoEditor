using System.Globalization;
using System.Numerics;

namespace FrameForge.Analysis;

/// <summary>
/// 64-bit DCT perceptual hash ("pHash" style): luminance is area-averaged to 32×32, transformed with a 2-D DCT-II,
/// and each of the 8×8 lowest-frequency coefficients becomes one bit (above / not above their median, DC excluded
/// from the median). Visually similar images, including resized or re-compressed copies, differ in few bits.
/// It is not robust to crops, rotations, or large edits.
/// </summary>
public static class PerceptualHasher
{
    public const string Version = "dct-phash-32x8-v1";
    private const int Size = 32;
    private const int Low = 8;

    private static readonly double[,] Cosines = BuildCosines();

    public static ulong Compute(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var small = Downsample(image.ToLuminance(), image.Width, image.Height);
        var dct = Dct2D(small);

        var coefficients = new double[Low * Low];
        for (var v = 0; v < Low; v++)
        {
            for (var u = 0; u < Low; u++)
            {
                coefficients[(v * Low) + u] = dct[v, u];
            }
        }

        var median = Median(coefficients.Skip(1).ToArray());
        ulong hash = 0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            if (coefficients[i] > median)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    public static string Format(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>Area-average (box filter) to 32×32, so every source pixel contributes.</summary>
    private static double[,] Downsample(float[] lum, int width, int height)
    {
        var result = new double[Size, Size];
        for (var ty = 0; ty < Size; ty++)
        {
            int y0 = ty * height / Size, y1 = Math.Max(y0 + 1, (ty + 1) * height / Size);
            for (var tx = 0; tx < Size; tx++)
            {
                int x0 = tx * width / Size, x1 = Math.Max(x0 + 1, (tx + 1) * width / Size);
                double sum = 0;
                for (var y = y0; y < Math.Min(y1, height); y++)
                {
                    for (var x = x0; x < Math.Min(x1, width); x++)
                    {
                        sum += lum[(y * width) + x];
                    }
                }

                result[ty, tx] = sum / ((Math.Min(y1, height) - y0) * (Math.Min(x1, width) - x0));
            }
        }

        return result;
    }

    /// <summary>Separable 2-D DCT-II; only the low-frequency block is needed, so only those rows/columns are computed.</summary>
    private static double[,] Dct2D(double[,] input)
    {
        var rows = new double[Size, Low];
        for (var y = 0; y < Size; y++)
        {
            for (var u = 0; u < Low; u++)
            {
                double sum = 0;
                for (var x = 0; x < Size; x++)
                {
                    sum += input[y, x] * Cosines[u, x];
                }

                rows[y, u] = sum;
            }
        }

        var output = new double[Low, Low];
        for (var v = 0; v < Low; v++)
        {
            for (var u = 0; u < Low; u++)
            {
                double sum = 0;
                for (var y = 0; y < Size; y++)
                {
                    sum += rows[y, u] * Cosines[v, y];
                }

                output[v, u] = sum;
            }
        }

        return output;
    }

    private static double[,] BuildCosines()
    {
        var table = new double[Low, Size];
        for (var k = 0; k < Low; k++)
        {
            for (var n = 0; n < Size; n++)
            {
                table[k, n] = Math.Cos(Math.PI / Size * (n + 0.5) * k);
            }
        }

        return table;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        var mid = values.Length / 2;
        return values.Length % 2 == 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }
}
