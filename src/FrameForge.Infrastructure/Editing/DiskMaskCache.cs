using System.IO.Compression;
using System.Text;
using FrameForge.Application.Editing.Masks;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Editing;

/// <summary>
/// Masks on disk under <see cref="AppDataPaths.MasksDirectory"/>, one file per <see cref="MaskKey"/>. Derived data:
/// safe to delete at any time, recomputed on demand. Written to a temporary file and moved into place, so a crash never
/// leaves half a mask under its final name; an unreadable file is deleted and treated as missing.
/// </summary>
/// <remarks>
/// Format (little-endian): "FFMK", format version (1 byte), width and height (int32 each), summary (length-prefixed
/// UTF-8), then the mask's bytes, Deflate-compressed (masks are mostly 0 and 255, so a 1024 px mask is a few tens of KB).
/// </remarks>
public sealed partial class DiskMaskCache(AppDataPaths paths, ILogger<DiskMaskCache> logger) : IMaskCache
{
    private const byte FormatVersion = 1;
    private const int MaxSide = 16_384;
    private static readonly byte[] Magic = "FFMK"u8.ToArray();

    public CachedMask? TryGet(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ArgumentException or UnauthorizedAccessException)
        {
            LogUnreadable(ex, key);
            TryDelete(path);
            return null;
        }
    }

    public void Save(string key, CachedMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Write(stream, mask);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    internal string PathFor(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length < 8 || !key.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A mask key is lower-case hex.", nameof(key));
        }

        return Path.Combine(paths.MasksDirectory, key[..2], key + ".mask");
    }

    internal static void Write(Stream stream, CachedMask mask)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(mask.Mask.Width);
        writer.Write(mask.Mask.Height);
        writer.Write(mask.Summary);
        writer.Flush();
        using var deflate = new DeflateStream(stream, CompressionLevel.Fastest, leaveOpen: true);
        deflate.Write(mask.Mask.Alpha);
    }

    internal static CachedMask Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) || reader.ReadByte() != FormatVersion)
        {
            throw new InvalidDataException("Not a FrameForge mask file, or an unknown version.");
        }

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
        {
            throw new InvalidDataException($"Unreasonable mask size {width} × {height}.");
        }

        var summary = reader.ReadString();
        var alpha = new byte[width * height];
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
        deflate.ReadExactly(alpha);
        if (deflate.ReadByte() != -1)
        {
            throw new InvalidDataException("The mask file has more data than its size says.");
        }

        return new CachedMask(new MaskImage(width, height, alpha), summary);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cached mask {Key} was unreadable and is computed again")]
    private partial void LogUnreadable(Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete mask cache file {Path}")]
    private partial void LogDeleteFailed(Exception exception, string path);
}
