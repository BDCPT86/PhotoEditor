using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Ai;

namespace FrameForge.Evaluation;

/// <summary>Draws detected faces onto a copy of the analysis image and saves it as a JPEG (originals are never touched).</summary>
internal static class Annotator
{
    public static void Save(AnalysisImage image, IReadOnlyList<DetectedFace> faces, float countedScore, string path)
    {
        var px = (byte[])image.Bgra.Clone();
        foreach (var f in faces)
        {
            // Counted faces green, below-threshold detections amber.
            var colour = f.Score >= countedScore ? ((byte)0, (byte)230, (byte)0) : ((byte)255, (byte)170, (byte)0);
            int x1 = (int)(f.X * image.Width), y1 = (int)(f.Y * image.Height);
            int x2 = (int)((f.X + f.Width) * image.Width), y2 = (int)((f.Y + f.Height) * image.Height);
            Rect(px, image.Width, image.Height, x1, y1, x2, y2, 3, colour);
            for (var k = 0; k < 10; k += 2)
            {
                int lx = (int)(f.Landmarks[k] * image.Width), ly = (int)(f.Landmarks[k + 1] * image.Height);
                Rect(px, image.Width, image.Height, lx - 2, ly - 2, lx + 2, ly + 2, 3, ((byte)255, (byte)0, (byte)0));
            }
        }

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, px, image.Width * 4);
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Rect(byte[] px, int w, int h, int x1, int y1, int x2, int y2, int thickness, (byte R, byte G, byte B) c)
    {
        for (var y = Math.Max(0, y1); y <= Math.Min(h - 1, y2); y++)
        {
            for (var x = Math.Max(0, x1); x <= Math.Min(w - 1, x2); x++)
            {
                var edge = x - x1 < thickness || x2 - x < thickness || y - y1 < thickness || y2 - y < thickness;
                if (edge)
                {
                    var i = ((y * w) + x) * 4;
                    px[i] = c.B;
                    px[i + 1] = c.G;
                    px[i + 2] = c.R;
                    px[i + 3] = 255;
                }
            }
        }
    }
}
