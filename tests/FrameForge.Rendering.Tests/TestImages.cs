using FrameForge.Analysis;
using FrameForge.Core.Editing;

namespace FrameForge.Rendering.Tests;

internal static class TestImages
{
    public static AnalysisImage Render(AnalysisImage source, EditRecipe recipe, int? maxLongSide = null) =>
        EditRenderer.Render(source, recipe, maxLongSide, TestContext.Current.CancellationToken);

    public static AnalysisImage Solid(int width, int height, byte r, byte g, byte b) =>
        AnalysisImage.Create(width, height, (_, _) => (r, g, b));

    /// <summary>A smooth colourful gradient with no hard edges (sampling differences stay small).</summary>
    public static AnalysisImage Gradient(int width, int height) => AnalysisImage.Create(width, height, (x, y) =>
        ((byte)(40 + (180 * x / (width - 1))), (byte)(30 + (190 * y / (height - 1))), (byte)(200 - (150 * (x + y) / (width + height - 2)))));

    public static (byte R, byte G, byte B) Pixel(AnalysisImage image, int x, int y)
    {
        var i = ((y * image.Width) + x) * 4;
        return (image.Bgra[i + 2], image.Bgra[i + 1], image.Bgra[i]);
    }

    public static double Linear(byte value)
    {
        var v = value / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>Box-downscales by an integer factor (a reference that is independent of the renderer).</summary>
    public static AnalysisImage BoxDown(AnalysisImage image, int factor)
    {
        int w = image.Width / factor, h = image.Height / factor;
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                for (var c = 0; c < 4; c++)
                {
                    var sum = 0;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        for (var dx = 0; dx < factor; dx++)
                        {
                            sum += image.Bgra[((((y * factor) + dy) * image.Width) + (x * factor) + dx) * 4 + c];
                        }
                    }

                    dst[((y * w) + x) * 4 + c] = (byte)Math.Round(sum / (double)(factor * factor));
                }
            }
        }

        return new AnalysisImage(w, h, dst);
    }
}
