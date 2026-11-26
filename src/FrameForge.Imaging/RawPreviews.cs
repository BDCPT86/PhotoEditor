using System.IO;
using FrameForge.Application.Photos;
using FrameForge.Raw;

namespace FrameForge.Imaging;

/// <summary>
/// The embedded full-size JPEG of a RAW file, which FrameForge shows, analyses and edits in place of the sensor data
/// (ADR 0020). <see cref="RawReader"/> is stateless and thread-safe, so one instance serves every caller.
/// </summary>
internal static class RawPreviews
{
    private static readonly RawReader Reader = new();

    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="PhotoReadException">It has no usable preview, can't be read, or RAW is unavailable here.</exception>
    public static RawPreview Read(string path)
    {
        try
        {
            return Reader.ReadPreview(path);
        }
        catch (RawReadException ex)
        {
            throw new PhotoReadException($"'{Path.GetFileName(path)}' can't be shown: {ex.Message}", ex);
        }
    }

    /// <summary>The RAW file's header details, for the EXIF of an edited JPEG (<see cref="RawExif"/>).</summary>
    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="PhotoReadException">It can't be read, or RAW is unavailable here.</exception>
    public static RawPhotoInfo ReadInfo(string path)
    {
        try
        {
            return Reader.ReadInfo(path);
        }
        catch (RawReadException ex)
        {
            throw new PhotoReadException($"'{Path.GetFileName(path)}' details can't be read: {ex.Message}", ex);
        }
    }
}
