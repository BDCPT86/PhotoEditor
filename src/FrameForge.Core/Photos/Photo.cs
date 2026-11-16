namespace FrameForge.Core.Photos;

/// <summary>
/// A photo in the library. Only the path and metadata are stored; the source file is never
/// copied or modified.
/// </summary>
public sealed class Photo
{
    public Guid Id { get; set; }

    /// <summary>Absolute, normalised path. Unique within the library (case-insensitive).</summary>
    public required string FilePath { get; set; }

    public required string FileName { get; set; }

    /// <summary>Absolute path of the containing folder.</summary>
    public required string FolderPath { get; set; }

    public long FileSizeBytes { get; set; }

    public DateTime LastModifiedUtc { get; set; }

    public DateTime ImportedUtc { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <inheritdoc cref="PhotoMetadata.CaptureLocalTime"/>
    public DateTime? CaptureLocalTime { get; set; }

    public string? CameraMake { get; set; }

    public string? CameraModel { get; set; }

    public string? LensModel { get; set; }

    public int? Iso { get; set; }

    public string? ExposureTime { get; set; }

    public double? FNumber { get; set; }

    public double? FocalLengthMm { get; set; }

    public int? Orientation { get; set; }

    public ImportStatus Status { get; set; }

    public string? LastError { get; set; }

    public void ApplyMetadata(PhotoMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Width = metadata.Width;
        Height = metadata.Height;
        CaptureLocalTime = metadata.CaptureLocalTime;
        CameraMake = metadata.CameraMake;
        CameraModel = metadata.CameraModel;
        LensModel = metadata.LensModel;
        Iso = metadata.Iso;
        ExposureTime = metadata.ExposureTime;
        FNumber = metadata.FNumber;
        FocalLengthMm = metadata.FocalLengthMm;
        Orientation = metadata.Orientation;
    }
}
