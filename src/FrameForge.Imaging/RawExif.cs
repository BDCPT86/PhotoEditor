using System.Globalization;
using System.Windows.Media.Imaging;
using FrameForge.Core.Photos;
using FrameForge.Raw;

namespace FrameForge.Imaging;

/// <summary>
/// EXIF for an edited JPEG of a RAW file, written from what LibRaw reads from the RAW file's header (ADR 0020).
/// The JPEG embedded in a CR3 is no source for it: LibRaw gives it a small EXIF block of its own ("dcraw v9.26" as the
/// software, the model without the make, no lens and no original capture time).
/// </summary>
/// <remarks>
/// Written: make, model (the EXIF way, "Canon EOS R50"), orientation 1 (the pixels are upright), capture time (as
/// DateTimeOriginal, DateTimeDigitized and DateTime), exposure time, f-number, ISO, focal length and lens.
/// Not written: GPS and anything else LibRaw doesn't give, and the colour space, which the embedded JPEG doesn't state.
/// </remarks>
internal static class RawExif
{
    private const string DateTimeDigitizedQuery = "/app1/ifd/exif/{ushort=36868}";

    public static BitmapMetadata Create(RawPhotoInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var metadata = new BitmapMetadata("jpg");

        SetText(metadata, WicPhotoMetadataReader.MakeQuery, info.CameraMake);
        SetText(metadata, WicPhotoMetadataReader.ModelQuery, CameraName.Format(info.CameraMake, info.CameraModel));
        metadata.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)1);

        if (info.CaptureLocalTime is { } taken)
        {
            var text = taken.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
            metadata.SetQuery(WicPhotoMetadataReader.DateTimeOriginalQuery, text);
            metadata.SetQuery(DateTimeDigitizedQuery, text);
            metadata.SetQuery(WicPhotoMetadataReader.DateTimeQuery, text);
        }

        if (ExposureRational(info.ExposureSeconds) is { } exposure)
        {
            metadata.SetQuery(WicPhotoMetadataReader.ExposureTimeQuery, exposure);
        }

        if (info.FNumber is > 0 and var f)
        {
            metadata.SetQuery(WicPhotoMetadataReader.FNumberQuery, Rational((uint)Math.Round(f * 10), 10));
        }

        if (info.Iso is > 0 and < ushort.MaxValue and var iso)
        {
            metadata.SetQuery(WicPhotoMetadataReader.IsoQuery, (ushort)Math.Round(iso));
        }

        if (info.FocalLengthMm is > 0 and var mm)
        {
            metadata.SetQuery(WicPhotoMetadataReader.FocalLengthQuery, Rational((uint)Math.Round(mm * 10), 10));
        }

        SetText(metadata, WicPhotoMetadataReader.LensModelQuery, info.Lens);
        return metadata;
    }

    /// <summary>"1/250" as 1/250 below a second; 2.5 s as 25/10.</summary>
    internal static ulong? ExposureRational(double? seconds) =>
        seconds switch
        {
            not > 0 => null,
            < 1 => Rational(1, (uint)Math.Round(1 / seconds.Value)),
            _ => Rational((uint)Math.Round(seconds.Value * 10), 10),
        };

    /// <summary>An unsigned EXIF rational as WIC takes it: numerator in the low 32 bits, denominator in the high.</summary>
    private static ulong Rational(uint numerator, uint denominator) => ((ulong)denominator << 32) | numerator;

    private static void SetText(BitmapMetadata metadata, string query, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata.SetQuery(query, value.Trim());
        }
    }
}
