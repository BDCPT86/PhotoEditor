using System.Text;
using FrameForge.Application.Export;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Export;

/// <summary>
/// Copies photos to a folder, or writes a manifest. Safety rules:
/// sources are opened read-only; destination files are created with <see cref="FileMode.CreateNew"/> under a
/// temporary name and moved into place without overwrite, so an existing file is never replaced even if it
/// appears mid-export; partial files are always removed.
/// </summary>
public sealed partial class FileSystemExportService(ILogger<FileSystemExportService> logger) : IExportService
{
    /// <summary>Suffix of in-progress export files; any such file is incomplete and safe to delete.</summary>
    public const string PartialFileSuffix = ".frameforge-partial";
    private const int BufferSize = 1024 * 1024;
    private const int MaxRenameAttempts = 1000;

    public Task<ExportSummary> CopyAsync(
        IReadOnlyList<ExportItem> items,
        string destinationFolder,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        return Task.Run(() => CopyCoreAsync(items, Path.GetFullPath(destinationFolder), progress, cancellationToken), CancellationToken.None);
    }

    public Task<ExportSummary> WriteManifestAsync(IReadOnlyList<ExportItem> items, string manifestPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        return Task.Run(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(manifestPath);
                if (items.Any(i => string.Equals(Path.GetFullPath(i.Photo.FilePath), path, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("The manifest cannot be written over one of the photos being exported.", nameof(manifestPath));
                }

                var directory = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(directory))
                {
                    throw new DirectoryNotFoundException($"Folder not found: {directory}");
                }

                var temp = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + PartialFileSuffix);
                try
                {
                    var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
                    await using (stream.ConfigureAwait(false))
                    {
                        // UTF-8 with BOM so spreadsheet apps detect the encoding of non-ASCII paths.
                        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                        await using (writer.ConfigureAwait(false))
                        {
                            ManifestWriter.Write(writer, items);
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // The user chose this path in a save dialog, which already confirmed replacing an existing manifest.
                    File.Move(temp, path, overwrite: true);
                }
                finally
                {
                    DeleteIfExists(temp);
                }

                LogManifestWritten(items.Count, path);
                return new ExportSummary { Destination = path, Requested = items.Count, Exported = items.Count };
            },
            CancellationToken.None);
    }

    private async Task<ExportSummary> CopyCoreAsync(
        IReadOnlyList<ExportItem> items,
        string destination,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destination))
        {
            throw new DirectoryNotFoundException($"Folder not found: {destination}");
        }

        var existing = Directory.EnumerateFileSystemEntries(destination).Select(e => Path.GetFileName(e)).ToList();
        var plan = ExportNamePlanner.Plan(items.Select(i => i.Photo.FilePath), existing);
        var taken = new HashSet<string>(existing.Concat(plan.Select(p => p.TargetFileName)), StringComparer.OrdinalIgnoreCase);

        var failures = new List<ExportFailure>();
        int exported = 0, renamed = 0, completed = 0;
        long bytes = 0;
        var cancelled = false;
        LogCopyStarting(plan.Count, destination);
        progress?.Report(new ExportProgress(plan.Count, 0, 0, 0, null));

        try
        {
            foreach (var file in plan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var (finalName, size) = await CopyOneAsync(file.SourcePath, destination, file.TargetFileName, taken, cancellationToken).ConfigureAwait(false);
                    exported++;
                    bytes += size;
                    if (!string.Equals(finalName, Path.GetFileName(file.SourcePath), StringComparison.Ordinal))
                    {
                        renamed++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new ExportFailure(file.SourcePath, ex.Message));
                    LogCopyFailed(ex, file.SourcePath);
                }

                completed++;
                progress?.Report(new ExportProgress(plan.Count, completed, failures.Count, bytes, Path.GetFileName(file.SourcePath)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        LogCopyFinished(exported, renamed, failures.Count, cancelled, destination);
        return new ExportSummary
        {
            Destination = destination,
            Requested = plan.Count,
            Exported = exported,
            Renamed = renamed,
            Failures = failures,
            BytesCopied = bytes,
            WasCancelled = cancelled,
        };
    }

    private static async Task<(string FinalName, long Size)> CopyOneAsync(
        string sourcePath,
        string destination,
        string targetName,
        HashSet<string> taken,
        CancellationToken cancellationToken)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            throw new FileNotFoundException("The source photo no longer exists.", sourcePath);
        }

        var temp = Path.Combine(destination, "." + Guid.NewGuid().ToString("N") + PartialFileSuffix);
        try
        {
            var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (input.ConfigureAwait(false))
            {
                var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
                await using (output.ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                }
            }

            File.SetLastWriteTimeUtc(temp, source.LastWriteTimeUtc);

            // Move into place without overwriting. If something created the name since planning,
            // take the next free name instead.
            var name = targetName;
            for (var attempt = 0; ; attempt++)
            {
                var final = Path.Combine(destination, name);
                try
                {
                    File.Move(temp, final, overwrite: false);
                    return (name, source.Length);
                }
                catch (IOException) when (attempt < MaxRenameAttempts && (File.Exists(final) || Directory.Exists(final)))
                {
                    taken.Add(name);
                    name = ExportNamePlanner.NextAvailable(Path.GetFileName(sourcePath), taken);
                    taken.Add(name);
                }
            }
        }
        finally
        {
            DeleteIfExists(temp);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Export started: {Count} photo(s) to {Destination}")]
    private partial void LogCopyStarting(int count, string destination);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Export could not copy {SourcePath}")]
    private partial void LogCopyFailed(Exception exception, string sourcePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Export finished: exported={Exported} renamed={Renamed} failed={Failed} cancelled={Cancelled} to {Destination}")]
    private partial void LogCopyFinished(int exported, int renamed, int failed, bool cancelled, string destination);

    [LoggerMessage(Level = LogLevel.Information, Message = "Manifest of {Count} photo(s) written to {Path}")]
    private partial void LogManifestWritten(int count, string path);
}
