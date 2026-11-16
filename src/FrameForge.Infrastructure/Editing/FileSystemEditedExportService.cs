using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Application.Photos;
using FrameForge.Infrastructure.Export;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Editing;

/// <summary>
/// Exports edited photos as new JPEGs. Same safety rules as <see cref="FileSystemExportService"/>: originals are only
/// read, output goes to a temporary file created with <see cref="FileMode.CreateNew"/> and is moved into place
/// without overwrite, and partial files are always removed. One photo at a time to bound memory (a 24 MP render
/// holds two 8-bit buffers, about 200 MB).
/// </summary>
public sealed partial class FileSystemEditedExportService(
    IEditRenderService renderer,
    IJpegEncoder encoder,
    ILogger<FileSystemEditedExportService> logger) : IEditedExportService
{
    private const int MaxRenameAttempts = 1000;

    public Task<ExportSummary> ExportAsync(
        IReadOnlyList<EditedExportItem> items,
        string destinationFolder,
        EditedExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate() is { Count: > 0 } problems)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(options));
        }

        return Task.Run(() => ExportCoreAsync(items, Path.GetFullPath(destinationFolder), options, progress, cancellationToken), CancellationToken.None);
    }

    private async Task<ExportSummary> ExportCoreAsync(
        IReadOnlyList<EditedExportItem> items,
        string destination,
        EditedExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destination))
        {
            throw new DirectoryNotFoundException($"Folder not found: {destination}");
        }

        // Output is always JPEG: name it .jpg even if the source used another extension.
        var existing = Directory.EnumerateFileSystemEntries(destination).Select(e => Path.GetFileName(e)).ToList();
        var plan = ExportNamePlanner.Plan(items.Select(i => Path.ChangeExtension(i.Photo.FileName, ".jpg")), existing);
        var taken = new HashSet<string>(existing.Concat(plan.Select(p => p.TargetFileName)), StringComparer.OrdinalIgnoreCase);

        var failures = new List<ExportFailure>();
        int exported = 0, renamed = 0, completed = 0, withoutMetadata = 0;
        long bytes = 0;
        var cancelled = false;
        LogStarting(items.Count, destination, options.Quality, options.MaxLongSide);
        progress?.Report(new ExportProgress(items.Count, 0, 0, 0, null));

        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                try
                {
                    var image = await renderer.RenderAsync(item.Photo, item.Recipe, options.MaxLongSide, cancellationToken).ConfigureAwait(false);
                    var (name, size, metadataKept) = WriteOne(image, item, destination, plan[i].TargetFileName, taken, options, cancellationToken);
                    exported++;
                    bytes += size;
                    renamed += plan[i].WasRenamed || !string.Equals(name, plan[i].TargetFileName, StringComparison.Ordinal) ? 1 : 0;
                    withoutMetadata += options.KeepMetadata && !metadataKept ? 1 : 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PhotoReadException)
                {
                    failures.Add(new ExportFailure(item.Photo.FilePath, ex.Message));
                    LogFailed(ex, item.Photo.FilePath);
                }

                completed++;
                progress?.Report(new ExportProgress(items.Count, completed, failures.Count, bytes, item.Photo.FileName));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        LogFinished(exported, failures.Count, withoutMetadata, cancelled, destination);
        return new ExportSummary
        {
            Destination = destination,
            Requested = items.Count,
            Exported = exported,
            Renamed = renamed,
            Failures = failures,
            BytesCopied = bytes,
            WasCancelled = cancelled,
        };
    }

    private (string Name, long Size, bool MetadataKept) WriteOne(
        FrameForge.Analysis.AnalysisImage image,
        EditedExportItem item,
        string destination,
        string targetName,
        HashSet<string> taken,
        EditedExportOptions options,
        CancellationToken cancellationToken)
    {
        var temp = Path.Combine(destination, "." + Guid.NewGuid().ToString("N") + FileSystemExportService.PartialFileSuffix);
        try
        {
            bool metadataKept;
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
            {
                metadataKept = encoder.Write(image, options.KeepMetadata ? item.Photo.FilePath : null, output, options.Quality);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var size = new FileInfo(temp).Length;
            var name = targetName;
            for (var attempt = 0; ; attempt++)
            {
                var final = Path.Combine(destination, name);
                try
                {
                    File.Move(temp, final, overwrite: false);
                    return (name, size, metadataKept);
                }
                catch (IOException) when (attempt < MaxRenameAttempts && (File.Exists(final) || Directory.Exists(final)))
                {
                    taken.Add(name);
                    name = ExportNamePlanner.NextAvailable(targetName, taken);
                    taken.Add(name);
                }
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited export started: {Count} photo(s) to {Destination} (quality {Quality}, max side {MaxSide})")]
    private partial void LogStarting(int count, string destination, int quality, int? maxSide);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Edited export failed for {SourcePath}")]
    private partial void LogFailed(Exception exception, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited export finished: exported={Exported} failed={Failed} withoutMetadata={WithoutMetadata} cancelled={Cancelled} to {Destination}")]
    private partial void LogFinished(int exported, int failed, int withoutMetadata, bool cancelled, string destination);
}
