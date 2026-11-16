using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Editing;

namespace FrameForge.Imaging;

/// <summary>
/// Writes edited pixels as a JPEG with WIC. Metadata (EXIF, XMP) is copied from the original when possible, with
/// orientation reset to "normal" because the pixels are already upright. If the metadata cannot be carried over the
/// image is still written, without it. The original file is only read.
/// </summary>
public sealed class WicJpegEncoder : IJpegEncoder
{
    private const string ExifOrientation = "/app1/ifd/{ushort=274}";
    private const string XmpOrientation = "/xmp/tiff:Orientation";
    private const string ExifThumbnail = "/app1/thumb";

    public bool Write(AnalysisImage image, string? metadataSourcePath, Stream destination, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null, image.Bgra, image.Width * 4);
        bitmap.Freeze();

        if (metadataSourcePath is not null)
        {
            try
            {
                // WIC reads cloned metadata lazily from the source, so the original stays open (read-only) until the
                // encode is done. Encode to memory first: if WIC rejects the metadata, nothing partial is written.
                using var source = WicFiles.OpenReadOnly(metadataSourcePath);
                if (PrepareMetadata(source) is { } metadata)
                {
                    using var buffer = new MemoryStream();
                    Encode(bitmap, metadata, buffer, quality);
                    buffer.Position = 0;
                    buffer.CopyTo(destination);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || WicFiles.IsDecodeFailure(ex))
            {
                // Fall through to writing without metadata.
            }
        }

        Encode(bitmap, null, destination, quality);
        return metadataSourcePath is null;
    }

    private static void Encode(BitmapSource bitmap, BitmapMetadata? metadata, Stream destination, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        encoder.Save(destination);
    }

    /// <summary>The source's metadata with orientation reset; valid only while <paramref name="stream"/> is open.</summary>
    private static BitmapMetadata? PrepareMetadata(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder is not JpegBitmapDecoder || decoder.Frames.Count == 0 || decoder.Frames[0].Metadata is not BitmapMetadata source)
        {
            return null;
        }

        var metadata = source.Clone();
        if (metadata.ContainsQuery(ExifOrientation))
        {
            metadata.SetQuery(ExifOrientation, (ushort)1);
        }

        if (metadata.ContainsQuery(XmpOrientation))
        {
            metadata.SetQuery(XmpOrientation, "1");
        }

        // The embedded EXIF thumbnail shows the unedited photo; drop it rather than mislead.
        if (metadata.ContainsQuery(ExifThumbnail))
        {
            metadata.RemoveQuery(ExifThumbnail);
        }

        return metadata;
    }
}
