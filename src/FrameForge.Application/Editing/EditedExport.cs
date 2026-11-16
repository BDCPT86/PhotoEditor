using FrameForge.Analysis;
using FrameForge.Application.Export;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Editing;

public sealed record EditedExportOptions
{
    public const int MinQuality = 50;
    public const int MaxQuality = 100;

    public static EditedExportOptions Default { get; } = new();

    /// <summary>JPEG quality, 50–100.</summary>
    public int Quality { get; init; } = 92;

    /// <summary>Optional limit on the longest side; null keeps full resolution (never upscales).</summary>
    public int? MaxLongSide { get; init; }

    /// <summary>Copy the original's EXIF/XMP (orientation reset, since pixels are already upright).</summary>
    public bool KeepMetadata { get; init; } = true;

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

        return errors;
    }
}

public sealed record EditedExportItem(Photo Photo, EditRecipe Recipe);

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

/// <summary>Encodes pixels as JPEG (implemented with Windows Imaging Component).</summary>
public interface IJpegEncoder
{
    /// <param name="metadataSourcePath">Photo whose metadata to copy (orientation reset to 1), or null for none.</param>
    /// <returns>False if metadata could not be copied (the image itself was still written).</returns>
    bool Write(AnalysisImage image, string? metadataSourcePath, Stream destination, int quality);
}
