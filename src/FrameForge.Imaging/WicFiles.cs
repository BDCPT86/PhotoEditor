using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using FrameForge.Application.Photos;

namespace FrameForge.Imaging;

/// <summary>Shared helpers for reading source images through WIC.</summary>
internal static class WicFiles
{
    /// <summary>
    /// Opens a source photo strictly read-only. Sharing allows other apps to keep the file open;
    /// FrameForge never writes to originals.
    /// </summary>
    public static FileStream OpenReadOnly(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 64 * 1024, FileOptions.SequentialScan);

    /// <summary>Reads only the first frame's header (dimensions, metadata) without decoding pixels.</summary>
    public static BitmapFrame OpenFrameHeader(Stream stream)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);

        if (decoder.Frames.Count == 0)
        {
            throw new PhotoReadException("The file contains no image frames.");
        }

        return decoder.Frames[0];
    }

    /// <summary>
    /// WIC surfaces corrupt or unsupported data through several exception types. These are
    /// translated to <see cref="PhotoReadException"/>; I/O errors (missing file, sharing
    /// violation) are deliberately not included so callers can tell them apart.
    /// </summary>
    public static bool IsDecodeFailure(Exception exception) =>
        exception is NotSupportedException
            or FileFormatException
            or ExternalException
            or ArgumentException
            or InvalidOperationException
            or OverflowException;

    public static PhotoReadException ToReadException(string path, Exception exception) =>
        new($"'{Path.GetFileName(path)}' is not a readable image: {exception.Message}", exception);
}
