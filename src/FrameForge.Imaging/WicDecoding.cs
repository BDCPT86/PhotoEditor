using System.Windows.Media.Imaging;

namespace FrameForge.Imaging;

internal static class WicDecoding
{
    /// <summary>
    /// Decodes a source image, optionally downscaled so its longest stored side is at most
    /// <paramref name="maxPixelSize"/>, then applies EXIF orientation. The result is frozen
    /// and fully materialised, so it is safe to hand across threads.
    /// </summary>
    /// <param name="maxPixelSize">Null decodes at full resolution.</param>
    public static BitmapSource DecodeOriented(string path, int? maxPixelSize)
    {
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

        BitmapImage decoded;
        using (var stream = WicFiles.OpenReadOnly(path))
        {
            decoded = new BitmapImage();
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
        }

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
