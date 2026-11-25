using System.Runtime.InteropServices;
using System.Text;

namespace FrameForge.Raw;

/// <summary>What FrameForge needs from a RAW file's header.</summary>
/// <param name="Width">Width of the photo as stored (before <paramref name="ExifOrientation"/> is applied).</param>
/// <param name="ExifOrientation">The rotation to display it upright, as an EXIF orientation value (1, 3, 6 or 8).</param>
/// <param name="CaptureLocalTime">When it was taken, in the camera's clock time (no time zone).</param>
public sealed record RawPhotoInfo(
    string CameraMake,
    string CameraModel,
    string? Lens,
    int Width,
    int Height,
    int ExifOrientation,
    double? Iso,
    double? ExposureSeconds,
    double? FNumber,
    double? FocalLengthMm,
    DateTime? CaptureLocalTime);

/// <summary>The camera's own full JPEG rendering stored in the RAW file, and how to rotate it upright.</summary>
public sealed record RawPreview(byte[] Jpeg, int ExifOrientation);

/// <summary>Reads RAW files: the header and the embedded JPEG. See <see cref="RawReader"/>.</summary>
public interface IRawReader
{
    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="RawReadException">It can't be read as a RAW file, or RAW isn't available on this computer.</exception>
    RawPhotoInfo ReadInfo(string path);

    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="RawReadException">There is no embedded JPEG, or the file can't be read.</exception>
    RawPreview ReadPreview(string path);
}

/// <summary>
/// Reads RAW files with LibRaw: the header (<see cref="ReadInfo"/>) and the embedded JPEG (<see cref="ReadPreview"/>).
/// Thread-safe: each call opens its own LibRaw handle and closes it. Original files are only read.
/// </summary>
/// <remarks>
/// Some values are read from LibRaw's structures at offsets taken from libraw_types.h of 0.22.3 (x64), which is why
/// <see cref="LibRawLibrary"/> refuses any other version:
/// <list type="bullet">
/// <item>libraw_data_t: image pointer (8 bytes), then libraw_image_sizes_t, whose <c>flip</c> is at byte 32 → 40.</item>
/// <item>libraw_iparams_t: <c>make</c> at 4 and <c>model</c> at 68, 64 bytes each.</item>
/// <item>libraw_imgother_t: iso_speed, shutter, aperture, focal_len (floats at 0–12), timestamp (64-bit time_t) at 16.</item>
/// <item>libraw_lensinfo_t: five floats, LensMake[128] at 20, <c>Lens</c>[128] at 148.</item>
/// </list>
/// </remarks>
public sealed class RawReader : IRawReader
{
    private const int FlipOffset = 8 + 32;

    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="RawReadException">It can't be read as a RAW file, or RAW isn't available on this computer.</exception>
    public RawPhotoInfo ReadInfo(string path)
    {
        using var file = Open(path);
        var handle = file.Handle;
        var iparams = LibRawNative.libraw_get_iparams(handle);
        var other = LibRawNative.libraw_get_imgother(handle);
        var lens = LibRawNative.libraw_get_lensinfo(handle);
        var timestamp = Marshal.ReadInt64(other, 16);

        return new RawPhotoInfo(
            ReadString(iparams, 4, 64),
            ReadString(iparams, 68, 64),
            ReadString(lens, 148, 128) is { Length: > 0 } lensName ? lensName : null,
            LibRawNative.libraw_get_iwidth(handle),
            LibRawNative.libraw_get_iheight(handle),
            ExifOrientationFromFlip(Marshal.ReadInt32(handle, FlipOffset)),
            Positive(ReadFloat(other, 0)),
            Positive(ReadFloat(other, 4)),
            Positive(ReadFloat(other, 8)),
            Positive(ReadFloat(other, 12)),
            // LibRaw turns the EXIF date (camera clock, no zone) into Unix time with this PC's zone; undo that.
            timestamp > 0 ? DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime, DateTimeKind.Unspecified) : null);
    }

    /// <exception cref="FileNotFoundException">The file isn't there.</exception>
    /// <exception cref="RawReadException">There is no embedded JPEG, or the file can't be read.</exception>
    public RawPreview ReadPreview(string path)
    {
        using var file = Open(path);
        var handle = file.Handle;
        var orientation = ExifOrientationFromFlip(Marshal.ReadInt32(handle, FlipOffset));
        var rc = LibRawNative.libraw_unpack_thumb(handle);
        if (rc != 0)
        {
            throw new RawReadException($"No preview could be read from this RAW file ({Error(rc)}).");
        }

        var image = LibRawNative.libraw_dcraw_make_mem_thumb(handle, out var error);
        if (image == IntPtr.Zero)
        {
            throw new RawReadException($"No preview could be read from this RAW file ({Error(error)}).");
        }

        try
        {
            // libraw_processed_image_t: int type (1 = JPEG); ushort height, width, colors, bits; unsigned data_size; data.
            var type = Marshal.ReadInt32(image, 0);
            if (type != 1)
            {
                throw new RawReadException("This RAW file's preview isn't a JPEG, which FrameForge can't show yet.");
            }

            var size = Marshal.ReadInt32(image, 12);
            var jpeg = new byte[size];
            Marshal.Copy(image + 16, jpeg, 0, size);
            return new RawPreview(jpeg, orientation);
        }
        finally
        {
            LibRawNative.libraw_dcraw_clear_mem(image);
        }
    }

    /// <summary>LibRaw's <c>flip</c> (0, 3, 5, 6) as the EXIF orientation that shows the photo upright.</summary>
    internal static int ExifOrientationFromFlip(int flip) => flip switch
    {
        3 => 3, // 180°
        5 => 8, // 90° anticlockwise
        6 => 6, // 90° clockwise
        _ => 1,
    };

    private static OpenFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LibRawLibrary.EnsureReady();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The RAW file was not found.", path);
        }

        var handle = LibRawNative.libraw_init(0);
        if (handle == IntPtr.Zero)
        {
            throw new RawReadException("The RAW library could not start (out of memory?).");
        }

        var rc = LibRawNative.libraw_open_wfile(handle, path);
        if (rc != 0)
        {
            LibRawNative.libraw_close(handle);
            throw new RawReadException($"This file can't be read as a RAW photo ({Error(rc)}).");
        }

        return new OpenFile(handle);
    }

    private static string Error(int code) => Marshal.PtrToStringAnsi(LibRawNative.libraw_strerror(code)) ?? $"error {code}";

    private static double? Positive(float value) => value > 0 && float.IsFinite(value) ? value : null;

    private static float ReadFloat(IntPtr p, int offset) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, offset));

    private static string ReadString(IntPtr p, int offset, int max)
    {
        var bytes = new byte[max];
        Marshal.Copy(p + offset, bytes, 0, max);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? max : end).Trim();
    }

    /// <summary>One LibRaw handle, closed when disposed.</summary>
    private sealed class OpenFile(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;

        public void Dispose() => LibRawNative.libraw_close(Handle);
    }
}
