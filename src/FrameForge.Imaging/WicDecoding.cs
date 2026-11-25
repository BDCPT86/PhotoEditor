using System.IO;
using System.Windows.Media.Imaging;
using FrameForge.Core.Photos;

namespace FrameForge.Imaging;

internal static class WicDecoding
{
    /// <summary>
    /// Decodes a source image, optionally downscaled so its longest stored side is at most
    /// <paramref name="maxPixelSize"/>, then applies EXIF orientation. The result is frozen
    /// and fully materialised, so it is safe to hand across threads.
    /// <para>
    /// For a RAW file (<see cref="SupportedPhotoFormats.IsRaw"/>) this is the embedded full-size JPEG, rotated by the
    /// RAW file's own orientation (ADR 0020). Thumbnails, the viewer, analysis, AI and edit rendering all come here,
    /// so they all see RAW files the same way.
    /// </para>
    /// </summary>
    /// <param name="maxPixelSize">Null decodes at full resolution.</param>
    public static BitmapSource DecodeOriented(string path, int? maxPixelSize)
    {
        if (SupportedPhotoFormats.IsRaw(path))
        {
            var preview = RawPreviews.Read(path);
            using var jpeg = new MemoryStream(preview.Jpeg, writable: false);
            return DecodeOriented(jpeg, maxPixelSize, preview.ExifOrientation);
        }

        int? orientation;
        int width;
        int height;
        using (var headerStream = WicFiles.OpenReadOnly(path))
        {
            var header = WicFiles.OpenFrameHeader(headerStream);
            width = header.PixelWidth;
            height = header.PixelHeight;
            orientation = WicPhotoMetadataReader.ReadOrientation(header.Metadata as BitmapMetadata);
        }

        using var stream = WicFiles.OpenReadOnly(path);
        return Decode(stream, width, height, maxPixelSize, orientation);
    }

    /// <summary>A JPEG in memory (a RAW file's preview), with its orientation given rather than read from EXIF.</summary>
    private static BitmapSource DecodeOriented(MemoryStream jpeg, int? maxPixelSize, int orientation)
    {
        var header = WicFiles.OpenFrameHeader(jpeg);
        var (width, height) = (header.PixelWidth, header.PixelHeight);
        jpeg.Position = 0;
        return Decode(jpeg, width, height, maxPixelSize, orientation);
    }

    private static BitmapSource Decode(Stream stream, int width, int height, int? maxPixelSize, int? orientation)
    {
        var decoded = new BitmapImage();
        decoded.BeginInit();
        decoded.StreamSource = stream;
        decoded.CacheOption = BitmapCacheOption.OnLoad; // read everything now so the stream can close
        if (maxPixelSize is { } max && Math.Max(width, height) > max)
        {
            // Setting only one dimension preserves aspect ratio.
            if (width >= height)
            {
                decoded.DecodePixelWidth = max;
            }
            else
            {
                decoded.DecodePixelHeight = max;
            }
        }

        decoded.EndInit();
        decoded.Freeze();

        var oriented = ExifOrientation.Apply(decoded, orientation);
        if (ReferenceEquals(oriented, decoded))
        {
            return decoded;
        }

        // Materialise the transform once here rather than on every render.
        var cached = new CachedBitmap(oriented, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        cached.Freeze();
        return cached;
    }
}
