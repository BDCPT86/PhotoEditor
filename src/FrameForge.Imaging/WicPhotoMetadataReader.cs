using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Imaging;

/// <summary>
/// Reads dimensions and common EXIF fields from JPEG files using raw EXIF tag queries.
/// Only the header and metadata blocks are read; pixels are not decoded.
/// </summary>
public sealed class WicPhotoMetadataReader : IPhotoMetadataReader
{
    // JPEG EXIF query paths: IFD0 tags and Exif sub-IFD tags.
    internal const string MakeQuery = "/app1/ifd/{ushort=271}";
    internal const string ModelQuery = "/app1/ifd/{ushort=272}";
    internal const string OrientationQuery = "/app1/ifd/{ushort=274}";
    internal const string DateTimeQuery = "/app1/ifd/{ushort=306}";
    internal const string ExposureTimeQuery = "/app1/ifd/exif/{ushort=33434}";
    internal const string FNumberQuery = "/app1/ifd/exif/{ushort=33437}";
    internal const string IsoQuery = "/app1/ifd/exif/{ushort=34855}";
    internal const string DateTimeOriginalQuery = "/app1/ifd/exif/{ushort=36867}";
    internal const string FocalLengthQuery = "/app1/ifd/exif/{ushort=37386}";
    internal const string LensModelQuery = "/app1/ifd/exif/{ushort=42036}";

    public PhotoMetadata Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = WicFiles.OpenReadOnly(filePath);
        try
        {
            var frame = WicFiles.OpenFrameHeader(stream);
            var metadata = frame.Metadata as BitmapMetadata;

            return new PhotoMetadata
            {
                Width = frame.PixelWidth,
                Height = frame.PixelHeight,
                CameraMake = ReadString(metadata, MakeQuery),
                CameraModel = ReadString(metadata, ModelQuery),
                LensModel = ReadString(metadata, LensModelQuery),
                Orientation = ReadOrientation(metadata),
                CaptureLocalTime = ParseExifDate(ReadString(metadata, DateTimeOriginalQuery))
                    ?? ParseExifDate(ReadString(metadata, DateTimeQuery)),
                Iso = ReadInt(metadata, IsoQuery) is { } iso && iso > 0 ? iso : null,
                ExposureTime = FormatExposureTime(ReadRational(metadata, ExposureTimeQuery)),
                FNumber = ToPositiveDouble(ReadRational(metadata, FNumberQuery)),
                FocalLengthMm = ToPositiveDouble(ReadRational(metadata, FocalLengthQuery)),
            };
        }
        catch (Exception ex) when (WicFiles.IsDecodeFailure(ex))
        {
            throw WicFiles.ToReadException(filePath, ex);
        }
    }

    internal static int? ReadOrientation(BitmapMetadata? metadata) =>
        ReadInt(metadata, OrientationQuery) is { } o && ExifOrientation.IsValid(o) ? o : null;

    internal static DateTime? ParseExifDate(string? value)
    {
        // EXIF format: "yyyy:MM:dd HH:mm:ss". Cameras without a set clock write zeros or blanks.
        if (string.IsNullOrWhiteSpace(value) || value.Length < 19)
        {
            return null;
        }

        return DateTime.TryParseExact(value[..19], "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;
    }

    internal static string? FormatExposureTime((uint Numerator, uint Denominator)? rational)
    {
        if (rational is not { Numerator: > 0, Denominator: > 0 } r)
        {
            return null;
        }

        var seconds = (double)r.Numerator / r.Denominator;
        if (seconds >= 1)
        {
            return seconds.ToString("0.#", CultureInfo.InvariantCulture);
        }

        var reciprocal = Math.Round(r.Denominator / (double)r.Numerator);
        return "1/" + reciprocal.ToString("0", CultureInfo.InvariantCulture);
    }

    private static double? ToPositiveDouble((uint Numerator, uint Denominator)? rational) =>
        rational is { Numerator: > 0, Denominator: > 0 } r ? Math.Round((double)r.Numerator / r.Denominator, 2) : null;

    private static string? ReadString(BitmapMetadata? metadata, string query)
    {
        var value = Query(metadata, query) switch
        {
            string s => s,
            string[] { Length: > 0 } array => array[0],
            _ => null,
        };

        value = value?.Trim('\0', ' ');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? ReadInt(BitmapMetadata? metadata, string query) =>
        Query(metadata, query) switch
        {
            ushort v => v,
            short v => v,
            uint v when v <= int.MaxValue => (int)v,
            int v => v,
            ushort[] { Length: > 0 } a => a[0],
            _ => null,
        };

    /// <summary>WIC returns an EXIF RATIONAL as a UInt64: numerator in the low 32 bits, denominator in the high 32 bits.</summary>
    private static (uint Numerator, uint Denominator)? ReadRational(BitmapMetadata? metadata, string query) =>
        Query(metadata, query) switch
        {
            ulong v => ((uint)(v & 0xFFFFFFFF), (uint)(v >> 32)),
            long v when v >= 0 => ((uint)(v & 0xFFFFFFFF), (uint)(v >> 32)),
            _ => null,
        };

    private static object? Query(BitmapMetadata? metadata, string query)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            return metadata.GetQuery(query);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or ExternalException)
        {
            // A malformed or absent metadata block for one tag must not fail the whole read.
            return null;
        }
    }
}
