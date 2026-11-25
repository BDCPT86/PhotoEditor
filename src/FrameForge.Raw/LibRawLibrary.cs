using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace FrameForge.Raw;

public enum LibRawState
{
    /// <summary>Loaded and the tested version: RAW files can be read.</summary>
    Ready,

    /// <summary>libraw.dll is not next to the app.</summary>
    NotFound,

    /// <summary>It is there but Windows can't load it (usually the Visual C++ runtime is missing).</summary>
    CannotLoad,

    /// <summary>A LibRaw version other than the tested one; its data layout may differ, so it isn't used.</summary>
    WrongVersion,
}

/// <param name="IsTestedBuild">False when the DLL is the right version but not the exact file FrameForge was tested with
/// (for example a rebuild, which the LGPL allows). It is used, and this is logged.</param>
public sealed record LibRawStatus(LibRawState State, string? Version, bool IsTestedBuild, string Message)
{
    public bool IsReady => State == LibRawState.Ready;
}

/// <summary>
/// Loads LibRaw once, from the app's own folder only (never from elsewhere on the PATH), and checks it. FrameForge
/// reads a few of LibRaw's data structures at fixed offsets (<see cref="RawReader"/>), which are only valid for
/// <see cref="TestedVersion"/>, so any other version is refused. A different build of the same version is accepted,
/// so the library stays replaceable as the LGPL requires (third_party/libraw/0.22.3/README.md, ADR 0020).
/// </summary>
public static class LibRawLibrary
{
    public const string FileName = "libraw.dll";
    public const string TestedVersion = "0.22.3";
    public const string TestedSha256 = "2c4df36d9011267fd3ffd5c23e80d69d5f42c3d503f03ac02448f5c8a7fe0fa2";

    private static int _resolverSet;

    private static readonly Lazy<LibRawStatus> Loaded = new(() => Load(Path.Combine(AppContext.BaseDirectory, FileName)), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Loads the library on first use; never throws.</summary>
    public static LibRawStatus Status => Loaded.Value;

    /// <exception cref="RawReadException">RAW files can't be read on this computer; the message says why.</exception>
    internal static void EnsureReady()
    {
        if (!Status.IsReady)
        {
            throw new RawReadException(Status.Message);
        }
    }

    internal static LibRawStatus Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LibRawStatus(LibRawState.NotFound, null, false, $"RAW files can't be read: {FileName} is missing from the FrameForge folder.");
        }

        IntPtr handle;
        try
        {
            handle = NativeLibrary.Load(path);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            return new LibRawStatus(LibRawState.CannotLoad, null, false,
                "RAW files can't be read on this computer: the RAW library could not start. Installing the Microsoft Visual C++ runtime (x64) usually fixes this; FrameForge's installer does it when needed.");
        }

        // The imports resolve to this handle. .NET allows one resolver per assembly, so only the first load sets it.
        if (Interlocked.Exchange(ref _resolverSet, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(LibRawLibrary).Assembly, (name, _, _) => name == LibRawNative.Library ? handle : IntPtr.Zero);
        }

        string version;
        try
        {
            version = Marshal.PtrToStringAnsi(LibRawNative.libraw_version()) ?? string.Empty;
        }
        catch (EntryPointNotFoundException)
        {
            return new LibRawStatus(LibRawState.WrongVersion, null, false, $"RAW files can't be read: {FileName} is not a LibRaw library FrameForge can use.");
        }

        // "0.22.3-Release": the number before any suffix must match exactly.
        var number = version.Split('-')[0];
        if (number != TestedVersion)
        {
            return new LibRawStatus(LibRawState.WrongVersion, version, false,
                $"RAW files can't be read: {FileName} is LibRaw {version}, but this version of FrameForge works only with LibRaw {TestedVersion}.");
        }

        var tested = string.Equals(Sha256(path), TestedSha256, StringComparison.OrdinalIgnoreCase);
        return new LibRawStatus(LibRawState.Ready, version, tested,
            tested ? $"LibRaw {version}" : $"LibRaw {version} (a different build from the one FrameForge was tested with)");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>A RAW file that can't be read: damaged, not a supported RAW file, or RAW unavailable on this computer.</summary>
public sealed class RawReadException : Exception
{
    public RawReadException()
    {
    }

    public RawReadException(string message)
        : base(message)
    {
    }

    public RawReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
