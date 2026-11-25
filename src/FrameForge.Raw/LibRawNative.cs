using System.Runtime.InteropServices;

namespace FrameForge.Raw;

/// <summary>
/// The parts of LibRaw's C API (libraw.h, 0.22) FrameForge uses. Only <see cref="LibRawLibrary"/> may call these:
/// it loads the DLL from the app's folder and checks it first.
/// </summary>
internal static partial class LibRawNative
{
    public const string Library = "libraw";

    [LibraryImport(Library)]
    public static partial IntPtr libraw_init(uint flags);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int libraw_open_wfile(IntPtr lr, string path);

    [LibraryImport(Library)]
    public static partial int libraw_unpack_thumb(IntPtr lr);

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
    public static partial int libraw_get_iwidth(IntPtr lr);

    [LibraryImport(Library)]
    public static partial int libraw_get_iheight(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_get_iparams(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_get_imgother(IntPtr lr);

    [LibraryImport(Library)]
    public static partial IntPtr libraw_get_lensinfo(IntPtr lr);
}
