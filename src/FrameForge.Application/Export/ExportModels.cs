using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Export;

/// <summary>A photo to export, with its decision (used by the manifest).</summary>
public sealed record ExportItem(Photo Photo, PhotoDecision Decision);

public sealed record ExportProgress(int Total, int Completed, int Failed, long BytesCopied, string? CurrentFile);

public sealed record ExportFailure(string SourcePath, string Reason);

public sealed record ExportSummary
{
    public required string Destination { get; init; }

    public int Requested { get; init; }

    public int Exported { get; init; }

    /// <summary>Exported under a different name because the original name was taken.</summary>
    public int Renamed { get; init; }

    public IReadOnlyList<ExportFailure> Failures { get; init; } = [];

    public long BytesCopied { get; init; }

    public bool WasCancelled { get; init; }
}

public interface IExportService
{
    /// <summary>
    /// Copies photos into <paramref name="destinationFolder"/>. Sources are only read. Existing files are never
    /// overwritten: clashing names get a " (n)" suffix. Cancellation stops after removing any partial file.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The destination does not exist.</exception>
    Task<ExportSummary> CopyAsync(
        IReadOnlyList<ExportItem> items,
        string destinationFolder,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Writes a CSV manifest. Refuses to write over any exported source photo.</summary>
    Task<ExportSummary> WriteManifestAsync(
        IReadOnlyList<ExportItem> items,
        string manifestPath,
        CancellationToken cancellationToken);
}
