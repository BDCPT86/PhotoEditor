namespace FrameForge.Analysis.Tests;

/// <summary>Deterministic synthetic images with known properties (no binary fixtures, no user photos).</summary>
internal static class Fixtures
{
    /// <summary>A textured "scene": overlapping sine patterns plus seeded fine noise. Rich in high-frequency detail.</summary>
    public static AnalysisImage Scene(int width = 320, int height = 240, int seed = 1, double phase = 0)
    {
        var random = new Random(seed);
        var noise = new byte[width * height];
        random.NextBytes(noise);
        return AnalysisImage.Create(width, height, (x, y) =>
        {
            var v = 128
                + (50 * Math.Sin((x * 0.21) + phase))
                + (35 * Math.Cos((y * 0.17) - phase))
                + (25 * Math.Sin((x + y) * 0.05))
                + ((noise[(y * width) + x] - 128) * 0.25);
            var g = Clamp(v);
            return (Clamp(v + 20), g, Clamp(v - 20));
        });
    }

    /// <summary>A different composition: vertical bands and a bright disc.</summary>
    public static AnalysisImage OtherScene(int width = 320, int height = 240) =>
        AnalysisImage.Create(width, height, (x, y) =>
        {
            var inDisc = Math.Pow(x - (width * 0.7), 2) + Math.Pow(y - (height * 0.3), 2) < Math.Pow(height * 0.2, 2);
            var v = inDisc ? 230 : (x / 40 % 2 == 0 ? 60 : 160);
            var c = Clamp(v);
            return (c, c, c);
        });

    public static AnalysisImage Solid(int width, int height, byte r, byte g, byte b) =>
        AnalysisImage.Create(width, height, (_, _) => (r, g, b));

    /// <summary>Box blur of the given radius (simulates defocus).</summary>
    public static AnalysisImage BoxBlur(AnalysisImage source, int radius, Func<int, int, bool>? where = null)
    {
        int w = source.Width, h = source.Height;
        var src = source.Bgra;
        var dst = (byte[])src.Clone();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (where is not null && !where(x, y))
                {
                    continue;
                }

                for (var c = 0; c < 3; c++)
                {
                    int sum = 0, n = 0;
                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            int sx = Math.Clamp(x + dx, 0, w - 1), sy = Math.Clamp(y + dy, 0, h - 1);
                            sum += src[(((sy * w) + sx) * 4) + c];
                            n++;
                        }
                    }

                    dst[(((y * w) + x) * 4) + c] = (byte)(sum / n);
                }
            }
        }

        return new AnalysisImage(w, h, dst);
    }

    /// <summary>Uniform brightness change (simulates a small exposure difference between burst frames).</summary>
    public static AnalysisImage Brighten(AnalysisImage source, int delta)
    {
        var dst = (byte[])source.Bgra.Clone();
        for (var i = 0; i < dst.Length; i++)
        {
            if (i % 4 != 3)
            {
                dst[i] = Clamp(dst[i] + delta);
            }
        }

        return new AnalysisImage(source.Width, source.Height, dst);
    }

    /// <summary>Nearest-neighbour resize (simulates a re-exported smaller copy).</summary>
    public static AnalysisImage Resize(AnalysisImage source, int width, int height) =>
        AnalysisImage.Create(width, height, (x, y) =>
        {
            int sx = x * source.Width / width, sy = y * source.Height / height;
            var i = ((sy * source.Width) + sx) * 4;
            return (source.Bgra[i + 2], source.Bgra[i + 1], source.Bgra[i]);
        });

    /// <summary>Adds seeded per-pixel noise of the given amplitude (simulates JPEG re-compression artefacts).</summary>
    public static AnalysisImage Jitter(AnalysisImage source, int amplitude, int seed)
    {
        var random = new Random(seed);
        var dst = (byte[])source.Bgra.Clone();
        for (var i = 0; i < dst.Length; i++)
        {
            if (i % 4 != 3)
            {
                dst[i] = Clamp(dst[i] + random.Next(-amplitude, amplitude + 1));
            }
        }

        return new AnalysisImage(source.Width, source.Height, dst);
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
