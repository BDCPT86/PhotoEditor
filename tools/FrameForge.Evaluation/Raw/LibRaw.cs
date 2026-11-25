using System.Runtime.InteropServices;
using System.Text;

namespace FrameForge.Evaluation.Raw;

/// <summary>
/// The LibRaw C API (libraw.h, version 0.22) for the M07 A1 spike: only the calls the spike needs. Struct fields are
/// read at fixed offsets taken from <c>libraw_types.h</c> of 0.22.3 (x64, MSVC packing), so this must be checked again
/// before it is used with another LibRaw version.
/// </summary>
internal static partial class LibRawNative
{
    public const string Library = "libraw";

    [LibraryImport(Library)]
    public static partial IntPtr libraw_init(uint flags);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int libraw_open_wfile(IntPtr lr, string path);

    [LibraryImport(Library)]
    public static partial int libraw_unpack(IntPtr lr);

    [LibraryImport(Library)]
    public static partial int libraw_unpack_thumb(IntPtr lr);

    [LibraryImport(Library)]
    public static partial int libraw_dcraw_process(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_dcraw_make_mem_image(IntPtr lr, out int error);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_dcraw_make_mem_thumb(IntPtr lr, out int error);

    [LibraryImport(Library)]
    public static partial void libraw_dcraw_clear_mem(IntPtr image);

    [LibraryImport(Library)]
    public static partial void libraw_close(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_strerror(int error);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_version();

    [LibraryImport(Library)]
    public static partial IntPtr libraw_cameraList();

    [LibraryImport(Library)]
    public static partial int libraw_cameraCount();

    [LibraryImport(Library)]
    public static partial void libraw_set_output_bps(IntPtr lr, int value);

    [LibraryImport(Library)]
    public static partial void libraw_set_no_auto_bright(IntPtr lr, int value);

    [LibraryImport(Library)]
    public static partial void libraw_set_user_mul(IntPtr lr, int index, float value);

    [LibraryImport(Library)]
    public static partial float libraw_get_cam_mul(IntPtr lr, int index);

    [LibraryImport(Library)]
    public static partial int libraw_get_raw_width(IntPtr lr);

    [LibraryImport(Library)]
    public static partial int libraw_get_raw_height(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_get_iparams(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_get_imgother(IntPtr lr);

    private static string? _loadedFrom;

    /// <summary>Points the <c>libraw</c> imports at a DLL on disk. Call once, before any other call.</summary>
    public static void Load(string dllPath)
    {
        if (_loadedFrom is not null)
        {
            return;
        }

        var handle = NativeLibrary.Load(dllPath);
        NativeLibrary.SetDllImportResolver(typeof(LibRawNative).Assembly, (name, _, _) => name == Library ? handle : IntPtr.Zero);
        _loadedFrom = dllPath;
    }

    public static string Version => Marshal.PtrToStringAnsi(libraw_version()) ?? "?";

    public static IReadOnlyList<string> Cameras()
    {
        var list = libraw_cameraList();
        var count = libraw_cameraCount();
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var p = Marshal.ReadIntPtr(list, i * IntPtr.Size);
            if (p == IntPtr.Zero)
            {
                break;
            }

            names.Add(Marshal.PtrToStringAnsi(p) ?? string.Empty);
        }

        return names;
    }

    public static string Error(int code) => Marshal.PtrToStringAnsi(libraw_strerror(code)) ?? code.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A RAW file LibRaw could not open or decode.</summary>
internal sealed class RawDecodeException(string message) : Exception(message);

/// <summary>What LibRaw reads from the file header.</summary>
internal sealed record RawInfo(string Make, string Model, int RawWidth, int RawHeight, int Flip, float Iso, float Shutter, float Aperture, float FocalLength, DateTimeOffset? Taken);

/// <summary>A decoded image: 8-bit RGB (3 bytes a pixel), top-down, or the embedded JPEG's bytes.</summary>
internal sealed record RawPixels(int Width, int Height, byte[] Rgb);

internal sealed record RawPreview(string Format, int Width, int Height, byte[] Data);

/// <summary>One open RAW file. Not thread-safe; one LibRaw handle per file.</summary>
internal sealed class RawFile : IDisposable
{
    // libraw_data_t: a pointer (image), then libraw_image_sizes_t; its "flip" is at byte 32 of that struct.
    private const int FlipOffset = 8 + 32;

    private IntPtr _handle;

    private RawFile(IntPtr handle) => _handle = handle;

    public static RawFile Open(string path)
    {
        var handle = LibRawNative.libraw_init(0);
        if (handle == IntPtr.Zero)
        {
            throw new RawDecodeException("libraw_init failed.");
        }

        var rc = LibRawNative.libraw_open_wfile(handle, path);
        if (rc != 0)
        {
            LibRawNative.libraw_close(handle);
            throw new RawDecodeException("open: " + LibRawNative.Error(rc));
        }

        return new RawFile(handle);
    }

    public RawInfo Info()
    {
        var iparams = LibRawNative.libraw_get_iparams(_handle);
        var other = LibRawNative.libraw_get_imgother(_handle);
        var timestamp = Marshal.ReadInt64(other, 16);
        return new RawInfo(
            ReadString(iparams, 4, 64),
            ReadString(iparams, 68, 64),
            LibRawNative.libraw_get_raw_width(_handle),
            LibRawNative.libraw_get_raw_height(_handle),
            Marshal.ReadInt32(_handle, FlipOffset),
            ReadFloat(other, 0),
            ReadFloat(other, 4),
            ReadFloat(other, 8),
            ReadFloat(other, 12),
            timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : null);
    }

    /// <summary>The largest preview the camera stored in the file, or null if there is none.</summary>
    public RawPreview? Preview()
    {
        if (LibRawNative.libraw_unpack_thumb(_handle) != 0)
        {
            return null;
        }

        var image = LibRawNative.libraw_dcraw_make_mem_thumb(_handle, out var error);
        if (image == IntPtr.Zero)
        {
            throw new RawDecodeException("preview: " + LibRawNative.Error(error));
        }

        try
        {
            var (type, height, width, _, _, data) = ReadProcessed(image);
            return new RawPreview(type == 1 ? "JPEG" : "bitmap", width, height, data);
        }
        finally
        {
            LibRawNative.libraw_dcraw_clear_mem(image);
        }
    }

    /// <summary>Demosaics the sensor data to 8-bit sRGB. LibRaw applies the orientation.</summary>
    public RawPixels Develop(bool cameraWhiteBalance, bool autoBright)
    {
        LibRawNative.libraw_set_output_bps(_handle, 8);
        LibRawNative.libraw_set_no_auto_bright(_handle, autoBright ? 0 : 1);
        if (cameraWhiteBalance)
        {
            // The same as dcraw's -w (use_camera_wb), which has no setter in the C API.
            for (var i = 0; i < 4; i++)
            {
                LibRawNative.libraw_set_user_mul(_handle, i, LibRawNative.libraw_get_cam_mul(_handle, i));
            }
        }

        Check(LibRawNative.libraw_unpack(_handle), "unpack");
        Check(LibRawNative.libraw_dcraw_process(_handle), "process");
        var image = LibRawNative.libraw_dcraw_make_mem_image(_handle, out var error);
        if (image == IntPtr.Zero)
        {
            throw new RawDecodeException("image: " + LibRawNative.Error(error));
        }

        try
        {
            var (_, height, width, colors, bits, data) = ReadProcessed(image);
            if (colors != 3 || bits != 8)
            {
                throw new RawDecodeException($"Unexpected output: {colors} colours, {bits} bits.");
            }

            return new RawPixels(width, height, data);
        }
        finally
        {
            LibRawNative.libraw_dcraw_clear_mem(image);
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            LibRawNative.libraw_close(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static void Check(int rc, string step)
    {
        if (rc != 0)
        {
            throw new RawDecodeException(step + ": " + LibRawNative.Error(rc));
        }
    }

    // libraw_processed_image_t: int type; ushort height, width, colors, bits; unsigned data_size; uchar data[].
    private static (int Type, int Height, int Width, int Colors, int Bits, byte[] Data) ReadProcessed(IntPtr image)
    {
        var type = Marshal.ReadInt32(image, 0);
        var height = (ushort)Marshal.ReadInt16(image, 4);
        var width = (ushort)Marshal.ReadInt16(image, 6);
        var colors = (ushort)Marshal.ReadInt16(image, 8);
        var bits = (ushort)Marshal.ReadInt16(image, 10);
        var size = Marshal.ReadInt32(image, 12);
        var data = new byte[size];
        Marshal.Copy(image + 16, data, 0, size);
        return (type, height, width, colors, bits, data);
    }

    private static float ReadFloat(IntPtr p, int offset) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, offset));

    private static string ReadString(IntPtr p, int offset, int max)
    {
        var bytes = new byte[max];
        Marshal.Copy(p + offset, bytes, 0, max);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, end < 0 ? max : end).Trim();
    }
}
