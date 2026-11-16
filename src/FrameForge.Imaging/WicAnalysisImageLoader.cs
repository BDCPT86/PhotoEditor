using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Analysis;

namespace FrameForge.Imaging;

/// <summary>
/// Decodes a photo for analysis: JPEG-domain downscaling during decode, EXIF orientation applied,
/// converted to BGRA32. The source file is opened read-only.
/// </summary>
public sealed class WicAnalysisImageLoader : IAnalysisImageLoader
{
    public AnalysisImage Load(string path, int maxPixelSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 16);

        try
        {
            var decoded = WicDecoding.DecodeOriented(path, maxPixelSize);
            var bgra = decoded.Format == PixelFormats.Bgra32 ? decoded : new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            return new AnalysisImage(width, height, pixels);
        }
        catch (Exception ex) when (WicFiles.IsDecodeFailure(ex))
        {
            throw WicFiles.ToReadException(path, ex);
        }
    }
}
