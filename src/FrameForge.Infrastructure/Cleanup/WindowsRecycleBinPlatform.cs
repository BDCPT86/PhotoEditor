using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FrameForge.Infrastructure.Cleanup;

/// <summary>
/// The Windows side of moving files to the Recycle Bin: drive and Recycle Bin settings, the item count, and the shell's
/// file operation.
/// </summary>
/// <remarks>
/// Microsoft's documentation doesn't say what the shell does with a file it can't recycle when told not to ask. So
/// this never relies on it: <see cref="RecycleBinService"/> refuses such files first. The operation also asks for the
/// shell's "will be deleted permanently" warning, which overrides "don't ask" (FOF_WANTNUKEWARNING), so a file that
/// slipped through would bring up a question rather than be deleted.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsRecycleBinPlatform : IRecycleBinPlatform
{
    private const string BitBucketVolumes = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume";
    private const string ExplorerPolicies = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    public RecycleDrive? GetDrive(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = Path.GetPathRoot(Path.GetFullPath(path));

        // Only drive-letter paths ("C:\"): a network share ("\\server\share") has no Recycle Bin.
        if (root is not { Length: 3 } || root[1] != ':' || !char.IsAsciiLetter(root[0]))
        {
            return null;
        }

        var drive = new DriveInfo(root);
        var isFixed = drive.DriveType == DriveType.Fixed;
        var isReady = drive.IsReady;
        return new RecycleDrive(root, isFixed, isReady, isFixed && isReady ? ReadSettings(root) : null);
    }

    public long? FileSize(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? file.Length : null;
    }

    public long? ItemsInRecycleBin(string root)
    {
        var info = new ShQueryRbInfo { Size = (uint)Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBinW(root, ref info) == 0 ? info.NumItems : null;
    }

    public ShellRecycleResult Recycle(string path)
    {
        if (!Environment.Is64BitProcess)
        {
            // The struct below has the 64-bit layout; on 32-bit Windows packs it differently.
            return new ShellRecycleResult(false, "Moving to the Recycle Bin needs the 64-bit app.");
        }

        // A full path, double-null terminated: with a relative path the shell deletes instead of recycling.
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + '\0');
        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FoDelete,
                From = from,
                Flags = FofAllowUndo | FofNoConfirmation | FofWantNukeWarning | FofSilent,
            };
            var code = SHFileOperationW(ref operation);
            return code == 0 && operation.AnyOperationsAborted == 0
                ? new ShellRecycleResult(true, null)
                : new ShellRecycleResult(false, operation.AnyOperationsAborted != 0
                    ? "The move was cancelled."
                    : $"Windows error 0x{code:X}.");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    /// <summary>The Recycle Bin settings Windows keeps per volume, or null when it has none for this drive.</summary>
    private static RecycleBinSettings? ReadSettings(string root)
    {
        var buffer = new char[64];
        if (!GetVolumeNameForVolumeMountPointW(root, buffer, (uint)buffer.Length))
        {
            return null; // e.g. a SUBST drive: not a volume of its own
        }

        // "\\?\Volume{guid}\" → "{guid}"
        var name = new string(buffer).TrimEnd('\0');
        var start = name.IndexOf('{', StringComparison.Ordinal);
        var end = name.IndexOf('}', StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey($@"{BitBucketVolumes}\{name[start..(end + 1)]}");
        if (key?.GetValue("MaxCapacity") is not int maxMb)
        {
            return null;
        }

        var nuke = key.GetValue("NukeOnDelete") is int n && n != 0;
        return new RecycleBinSettings(!nuke && !RecyclingTurnedOffByPolicy(), maxMb * 1024L * 1024L);
    }

    /// <summary>The "Do not move deleted files to the Recycle Bin" policy.</summary>
    private static bool RecyclingTurnedOffByPolicy()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(ExplorerPolicies);
            if (key?.GetValue("NoRecycleFiles") is int value && value != 0)
            {
                return true;
            }
        }

        return false;
    }

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofWantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public uint Size;
        public long TotalBytes;
        public long NumItems;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(ref ShFileOpStruct operation);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string rootPath, ref ShQueryRbInfo info);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPointW(string mountPoint, [Out] char[] volumeName, uint length);
}
