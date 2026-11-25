using System.Globalization;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Raw;

namespace FrameForge.Imaging;

/// <summary>
/// Reads a RAW file's details through LibRaw (ADR 0020). The size is the photo as stored, before orientation, like
/// EXIF for a JPEG; the embedded preview has the same size on the Canon EOS R50.
/// </summary>
public sealed class RawPhotoMetadataReader(IRawReader raw) : IPhotoMetadataReader
{
    public PhotoMetadata Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        RawPhotoInfo info;
        try
        {
            info = raw.ReadInfo(filePath);
        }
        catch (RawReadException ex)
        {
            throw new PhotoReadException(ex.Message, ex);
        }

        return new PhotoMetadata
        {
            Width = info.Width > 0 ? info.Width : null,
            Height = info.Height > 0 ? info.Height : null,
            CaptureLocalTime = info.CaptureLocalTime,
            CameraMake = Clean(info.CameraMake),
            // LibRaw gives "EOS R50"; Canon's EXIF (and so its JPEGs) says "Canon EOS R50". Store it the EXIF way, so a
            // RAW file and its JPEG name the camera the same (filters, the {camera} file-name token).
            CameraModel = Clean(CameraName.Format(info.CameraMake, info.CameraModel)),
            LensModel = Clean(info.Lens),
            Iso = info.Iso is { } iso ? (int)Math.Round(iso) : null,
            ExposureTime = FormatExposure(info.ExposureSeconds),
            FNumber = info.FNumber is { } f ? Math.Round(f, 2) : null,
            FocalLengthMm = info.FocalLengthMm is { } mm ? Math.Round(mm, 2) : null,
            Orientation = info.ExifOrientation,
        };
    }

    /// <summary>Seconds as a JPEG's EXIF reader shows them: "1/250", or "2.5" for a second or more.</summary>
    internal static string? FormatExposure(double? seconds)
    {
        if (seconds is not > 0)
        {
            return null;
        }

        return seconds >= 1
            ? seconds.Value.ToString("0.#", CultureInfo.InvariantCulture)
            : "1/" + Math.Round(1 / seconds.Value).ToString("0", CultureInfo.InvariantCulture);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Sends RAW files to <see cref="RawPhotoMetadataReader"/> and everything else to WIC.</summary>
public sealed class PhotoMetadataReader(WicPhotoMetadataReader wic, RawPhotoMetadataReader raw) : IPhotoMetadataReader
{
    public PhotoMetadata Read(string filePath) =>
        SupportedPhotoFormats.IsRaw(filePath) ? raw.Read(filePath) : wic.Read(filePath);
}
