using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Export;

/// <summary>A photo to export, with its decision (used by the manifest) and edit (used when exporting edited JPEGs).</summary>
public sealed record ExportItem(Photo Photo, PhotoDecision Decision, EditRecipe? Recipe = null);

public sealed record ExportProgress(int Total, int Completed, int Failed, long BytesCopied, string? CurrentFile);

public sealed record ExportFailure(string SourcePath, string Reason);

/// <summary>A photo that was exported, but with part of its edit left out, and why (e.g. a mask that could not be made).</summary>
public sealed record ExportNote(string SourcePath, string Note);

public sealed record ExportSummary
{
    public required string Destination { get; init; }

    public int Requested { get; init; }

    public int Exported { get; init; }

    /// <summary>Exported with " (n)" added because the planned name was taken.</summary>
    public int Renamed { get; init; }

    public IReadOnlyList<ExportFailure> Failures { get; init; } = [];

    /// <summary>Photos exported without one of their masks (edited JPEGs), with the reason.</summary>
    public IReadOnlyList<ExportNote> Notes { get; init; } = [];

    public long BytesCopied { get; init; }

    public bool WasCancelled { get; init; }
}

public interface IExportService
{
    /// <summary>
    /// Copies photos into <paramref name="destinationFolder"/>, named by <paramref name="naming"/> (each keeps its own
    /// extension). Sources are only read. Existing files are never overwritten: clashing names get a " (n)" suffix.
    /// Cancellation stops after removing any partial file.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The destination does not exist.</exception>
    Task<ExportSummary> CopyAsync(
        IReadOnlyList<ExportItem> items,
        string destinationFolder,
        ExportNameTemplate naming,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Writes a CSV manifest. Refuses to write over any exported source photo.</summary>
    Task<ExportSummary> WriteManifestAsync(
        IReadOnlyList<ExportItem> items,
        string manifestPath,
        CancellationToken cancellationToken);
}
