using System.Windows.Media.Imaging;

namespace FrameForge.Imaging;

/// <summary>
/// Loads images for display. Returned bitmaps are frozen, so they may be created on a
/// background thread and bound on the UI thread.
/// </summary>
public interface IPhotoBitmapLoader
{
    /// <summary>Loads a cached thumbnail file (already oriented).</summary>
    BitmapSource LoadThumbnail(string thumbnailPath);

    /// <summary>Decodes a source photo at full resolution with EXIF orientation applied.</summary>
    /// <exception cref="FrameForge.Application.Photos.PhotoReadException">The file is not a readable image.</exception>
    /// <exception cref="System.IO.FileNotFoundException">The file does not exist.</exception>
    BitmapSource LoadFull(string photoPath, CancellationToken cancellationToken);
}
