namespace FrameForge.Core.Photos;

/// <summary>
/// Basic properties read from an image file. Every field is optional: cameras and editors
/// write EXIF inconsistently, and a missing field must never fail an import.
/// </summary>
public sealed record PhotoMetadata
{
    public static PhotoMetadata Empty { get; } = new();

    /// <summary>Stored pixel width, before EXIF orientation is applied.</summary>
    public int? Width { get; init; }

    /// <summary>Stored pixel height, before EXIF orientation is applied.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// EXIF DateTimeOriginal as the camera's local wall-clock time. EXIF has no reliable
    /// time zone, so this is deliberately not converted to UTC.
    /// </summary>
    public DateTime? CaptureLocalTime { get; init; }

    public string? CameraMake { get; init; }

    public string? CameraModel { get; init; }

    public string? LensModel { get; init; }

    public int? Iso { get; init; }

    /// <summary>Human-readable exposure time, e.g. "1/250" or "2.5".</summary>
    public string? ExposureTime { get; init; }

    public double? FNumber { get; init; }

    public double? FocalLengthMm { get; init; }

    /// <summary>EXIF orientation 1–8, or null when absent.</summary>
    public int? Orientation { get; init; }
}
