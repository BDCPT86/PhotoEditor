using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameForge.Imaging.Tests;

/// <summary>
/// Generates small JPEG fixtures at test time (no binary fixtures, no user photos).
/// The left half is red and the right half blue, so orientation can be verified by sampling pixels.
/// </summary>
internal static class TestJpeg
{
    public static readonly Color Left = Color.FromRgb(220, 30, 30);
    public static readonly Color Right = Color.FromRgb(30, 30, 220);

    public static void Write(string path, int width, int height, Action<BitmapMetadata>? writeMetadata = null)
    {
        const int bytesPerPixel = 3;
        var stride = width * bytesPerPixel;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = x < width / 2 ? Left : Right;
                var i = (y * stride) + (x * bytesPerPixel);
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
            }
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        BitmapMetadata? metadata = null;
        if (writeMetadata is not null)
        {
            metadata = new BitmapMetadata("jpg");
            writeMetadata(metadata);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Packs an EXIF RATIONAL the way WIC expects: numerator low 32 bits, denominator high 32 bits.</summary>
    public static ulong Rational(uint numerator, uint denominator) => ((ulong)denominator << 32) | numerator;

    public static Color SamplePixel(BitmapSource bitmap, int x, int y)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }

    public static bool IsRedish(Color c) => c.R > 150 && c.B < 100;

    public static bool IsBlueish(Color c) => c.B > 150 && c.R < 100;
}
