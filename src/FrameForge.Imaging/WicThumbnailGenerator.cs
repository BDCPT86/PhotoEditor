using System.IO;
using System.Windows.Media.Imaging;
using FrameForge.Application.Photos;

namespace FrameForge.Imaging;

/// <summary>
/// Creates JPEG thumbnails with WIC. Decoding uses <see cref="BitmapImage.DecodePixelWidth"/>,
/// which lets the JPEG decoder scale during decode instead of materialising full resolution.
/// </summary>
public sealed class WicThumbnailGenerator : IThumbnailGenerator
{
    private const int JpegQuality = 85;

    public string Version => "wic-jpeg-q85-v1";

    public void Generate(string sourcePath, Stream destination, int maxPixelSize, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 16);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var thumbnail = WicDecoding.DecodeOriented(sourcePath, maxPixelSize);
            cancellationToken.ThrowIfCancellationRequested();

            var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
            encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            encoder.Save(destination);
        }
        catch (Exception ex) when (WicFiles.IsDecodeFailure(ex))
        {
            throw WicFiles.ToReadException(sourcePath, ex);
        }
    }
}
