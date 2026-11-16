using System.Windows.Media.Imaging;

namespace FrameForge.Imaging;

public sealed class WicPhotoBitmapLoader : IPhotoBitmapLoader
{
    public BitmapSource LoadThumbnail(string thumbnailPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailPath);

        try
        {
            using var stream = WicFiles.OpenReadOnly(thumbnailPath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (WicFiles.IsDecodeFailure(ex))
        {
            throw WicFiles.ToReadException(thumbnailPath, ex);
        }
    }

    public BitmapSource LoadFull(string photoPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(photoPath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // WIC decoding cannot be interrupted part-way; cancellation is checked around it.
            var bitmap = WicDecoding.DecodeOriented(photoPath, maxPixelSize: null);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (Exception ex) when (WicFiles.IsDecodeFailure(ex))
        {
            throw WicFiles.ToReadException(photoPath, ex);
        }
    }
}
