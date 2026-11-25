using FrameForge.Analysis;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Editing;

/// <summary>Which of the original's metadata an edited JPEG carries. Copied originals are always byte-for-byte identical.</summary>
public enum ExportMetadata
{
    /// <summary>EXIF, XMP and IPTC from the original (see ADR 0017 for what is adjusted).</summary>
    All,

    /// <summary>As <see cref="All"/>, without GPS and place names (EXIF GPS, XMP and IPTC location fields).</summary>
    AllButLocation,

    /// <summary>No metadata at all.</summary>
    None,
}

public sealed record EditedExportOptions
{
    public const int MinQuality = 50;
    public const int MaxQuality = 100;

    public static EditedExportOptions Default { get; } = new();

    /// <summary>JPEG quality, 50–100.</summary>
    public int Quality { get; init; } = 92;

    /// <summary>Optional limit on the longest side; null keeps full resolution (never upscales).</summary>
    public int? MaxLongSide { get; init; }

    /// <summary>Which of the original's metadata to carry over.</summary>
    public ExportMetadata Metadata { get; init; } = ExportMetadata.All;

    /// <summary>
    /// Write the FrameForge star rating and colour label (EXIF/XMP rating, XMP label), replacing any in the original.
    /// Ignored with <see cref="ExportMetadata.None"/>.
    /// </summary>
    public bool WriteRatingAndLabel { get; init; } = true;

    /// <summary>How the JPEGs are named (always with a ".jpg" extension).</summary>
    public ExportNameTemplate Naming { get; init; } = ExportNameTemplate.Original;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Quality is < MinQuality or > MaxQuality)
        {
            errors.Add($"JPEG quality must be {MinQuality}–{MaxQuality}.");
        }

        if (MaxLongSide is < 64 or > 65_000)
        {
            errors.Add("The longest side must be 64–65,000 pixels.");
        }

        if (!Enum.IsDefined(Metadata))
        {
            errors.Add("Unknown metadata choice.");
        }

        return errors;
    }

    /// <summary>What the encoder should write for one photo under these options.</summary>
    public JpegMetadata MetadataFor(Photo photo, PhotoDecision? decision)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return Metadata == ExportMetadata.None
            ? JpegMetadata.Nothing
            : new JpegMetadata
            {
                SourcePath = photo.FilePath,
                RemoveLocation = Metadata == ExportMetadata.AllButLocation,
                Decision = WriteRatingAndLabel ? decision ?? PhotoDecision.Unset : null,
            };
    }
}

/// <summary>A photo to render. <paramref name="Decision"/> is used by name templates and for the rating and label.</summary>
public sealed record EditedExportItem(Photo Photo, EditRecipe Recipe, PhotoDecision? Decision = null);

public interface IEditedExportService
{
    /// <summary>
    /// Renders each photo with its recipe and writes a new JPEG into <paramref name="destinationFolder"/>. Originals
    /// are only read. Existing files are never overwritten (" (n)" suffix). Progress per photo; cancellation removes
    /// the partial file and keeps the finished ones. One failure does not stop the rest.
    /// </summary>
    Task<ExportSummary> ExportAsync(
        IReadOnlyList<EditedExportItem> items,
        string destinationFolder,
        EditedExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>The metadata to put in one edited JPEG.</summary>
public sealed record JpegMetadata
{
    /// <summary>A JPEG with no metadata.</summary>
    public static JpegMetadata Nothing { get; } = new();

    /// <summary>Photo whose metadata to copy, or null for none.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Leave out GPS and place names.</summary>
    public bool RemoveLocation { get; init; }

    /// <summary>When set, its rating and label replace the original's (unrated / no label removes them).</summary>
    public PhotoDecision? Decision { get; init; }
}

/// <summary>Encodes pixels as JPEG (implemented with Windows Imaging Component).</summary>
public interface IJpegEncoder
{
    /// <summary>Writes <paramref name="image"/> (sRGB pixels) as a JPEG.</summary>
    /// <returns>False if the source's metadata could not be copied (the image itself was still written).</returns>
    bool Write(AnalysisImage image, JpegMetadata metadata, Stream destination, int quality);
}
