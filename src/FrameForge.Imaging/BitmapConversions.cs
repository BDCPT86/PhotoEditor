using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;

namespace FrameForge.Imaging;

/// <summary>Converts between WPF bitmaps and the 8-bit BGRA buffers the renderer works on.</summary>
public static class BitmapConversions
{
    public static AnalysisImage ToBuffer(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var bgra = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new AnalysisImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }

    /// <summary>A frozen bitmap (safe to pass from a worker thread to the UI).</summary>
    public static BitmapSource ToBitmap(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null, image.Bgra, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
