using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Photos;

public sealed record PhotoImportOptions
{
    /// <summary>Files read concurrently. Bounded to keep disk and CPU usage predictable.</summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>Files read before results are written to the database in one transaction.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>Minimum interval between progress reports.</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}

public sealed partial class PhotoImportService(
    IPhotoFileEnumerator enumerator,
    IPhotoMetadataReader metadataReader,
    IPhotoRepository repository,
    PhotoImportOptions options,
    TimeProvider timeProvider,
    ILogger<PhotoImportService> logger) : IPhotoImportService
{
    public const string MissingReason = "File was not found when its folder was last imported.";

    public async Task<ImportSummary> ImportFolderAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var startedAt = timeProvider.GetTimestamp();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        LogStarting(root, includeSubfolders);

        var tally = new Tally();
        var reporter = new ProgressReporter(progress, timeProvider, options.ProgressInterval);
        PhotoFolderScan? scan = null;
        var cancelled = false;

        try
        {
            reporter.Report(new ImportProgress(ImportPhase.Scanning, 0, 0, 0, 0), force: true);
            scan = await Task.Run(() => enumerator.Scan(root, includeSubfolders, cancellationToken), cancellationToken).ConfigureAwait(false);
            foreach (var skipped in scan.SkippedFolders)
            {
                LogFolderSkipped(skipped);
            }

            var existing = await repository.GetFileStatesAsync(cancellationToken).ConfigureAwait(false);
            var existingByPath = BuildPathIndex(existing);

            var work = new List<(PhotoFileInfo File, PhotoFileState? Existing)>();
            foreach (var file in scan.Files)
            {
                existingByPath.TryGetValue(file.FilePath, out var state);
                if (IsUnchanged(file, state))
                {
                    tally.Unchanged++;
                }
                else
                {
                    work.Add((file, state));
                }
            }

            reporter.Begin(scan.Files.Count, work.Count);

            foreach (var batch in work.Chunk(options.BatchSize))
            {
                await ProcessBatchAsync(batch, nowUtc, tally, reporter, cancellationToken).ConfigureAwait(false);
            }

            reporter.Report(new ImportProgress(ImportPhase.Finishing, scan.Files.Count, work.Count, work.Count, tally.Failed), force: true);
            tally.MarkedMissing = await MarkMissingAsync(root, includeSubfolders, scan, existing).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }

        var summary = new ImportSummary
        {
            FolderPath = root,
            Discovered = scan?.Files.Count ?? 0,
            Added = tally.Added,
            Updated = tally.Updated,
            Unchanged = tally.Unchanged,
            Failed = tally.Failed,
            MarkedMissing = tally.MarkedMissing,
            SkippedFolders = scan?.SkippedFolders ?? [],
            WasCancelled = cancelled,
            Duration = timeProvider.GetElapsedTime(startedAt),
        };

        LogFinished(summary.FolderPath, summary.WasCancelled, summary.Discovered, summary.Added, summary.Updated, summary.Unchanged, summary.Failed, summary.MarkedMissing, summary.Duration.TotalSeconds);
        return summary;
    }

    private async Task ProcessBatchAsync(
        (PhotoFileInfo File, PhotoFileState? Existing)[] batch,
        DateTime nowUtc,
        Tally tally,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = new Photo?[batch.Length];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxParallelism,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Task.Run(
                () => Parallel.For(0, batch.Length, parallelOptions, i =>
                {
                    var photo = ReadPhoto(batch[i].File, batch[i].Existing, nowUtc);
                    results[i] = photo;
                    reporter.FileProcessed(photo.Status == ImportStatus.Failed);
                }),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // Persist whatever was read, even when cancelled, so the next import resumes
            // from here. Writes are not cancellable: a batch is small and must not be half-applied.
            var completed = new List<Photo>(batch.Length);
            for (var i = 0; i < batch.Length; i++)
            {
                if (results[i] is { } photo)
                {
                    completed.Add(photo);
                    tally.Record(photo, isNew: batch[i].Existing is null);
                }
            }

            if (completed.Count > 0)
            {
                await repository.UpsertAsync(completed, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private Photo ReadPhoto(PhotoFileInfo file, PhotoFileState? existing, DateTime nowUtc)
    {
        var photo = new Photo
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            FilePath = file.FilePath,
            FileName = Path.GetFileName(file.FilePath),
            FolderPath = Path.GetDirectoryName(file.FilePath) ?? file.FilePath,
            FileSizeBytes = file.FileSizeBytes,
            LastModifiedUtc = file.LastModifiedUtc,
            ImportedUtc = nowUtc,
        };

        try
        {
            photo.ApplyMetadata(metadataReader.Read(file.FilePath));
            photo.Status = ImportStatus.Imported;
            photo.LastError = null;
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            photo.Status = ImportStatus.Failed;
            photo.LastError = ex.Message;
            LogFileFailed(ex, file.FilePath);
        }

        return photo;
    }

    private async Task<int> MarkMissingAsync(string root, bool includeSubfolders, PhotoFolderScan scan, IReadOnlyList<PhotoFileState> existing)
    {
        var found = new HashSet<string>(scan.Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);

        var missing = existing
            .Where(s => s.Status != ImportStatus.Missing
                && FolderScope.IsInImportScope(s.FolderPath, root, includeSubfolders)
                && !scan.SkippedFolders.Any(skipped => FolderScope.IsSameOrUnder(s.FolderPath, skipped))
                && !found.Contains(s.FilePath))
            .Select(s => s.Id)
            .ToList();

        if (missing.Count > 0)
        {
            await repository.MarkMissingAsync(missing, MissingReason, CancellationToken.None).ConfigureAwait(false);
        }

        return missing.Count;
    }

    private static Dictionary<string, PhotoFileState> BuildPathIndex(IReadOnlyList<PhotoFileState> states)
    {
        var index = new Dictionary<string, PhotoFileState>(states.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var state in states)
        {
            index.TryAdd(state.FilePath, state);
        }

        return index;
    }

    private static bool IsUnchanged(PhotoFileInfo file, PhotoFileState? state) =>
        state is { Status: ImportStatus.Imported }
        && state.FileSizeBytes == file.FileSizeBytes
        && state.LastModifiedUtc == file.LastModifiedUtc;

    private sealed class Tally
    {
        public int Added { get; private set; }

        public int Updated { get; private set; }

        public int Failed { get; private set; }

        public int Unchanged { get; set; }

        public int MarkedMissing { get; set; }

        public void Record(Photo photo, bool isNew)
        {
            if (photo.Status == ImportStatus.Failed)
            {
                Failed++;
            }
            else if (isNew)
            {
                Added++;
            }
            else
            {
                Updated++;
            }
        }
    }

    /// <summary>Thread-safe, throttled progress reporting.</summary>
    private sealed class ProgressReporter(IProgress<ImportProgress>? progress, TimeProvider timeProvider, TimeSpan interval)
    {
        private readonly Lock _gate = new();
        private long _lastReport = long.MinValue;
        private int _lastProcessed;
        private int _discovered;
        private int _total;
        private int _processed;
        private int _failed;

        public void Begin(int discovered, int total)
        {
            _discovered = discovered;
            _total = total;
            Report(new ImportProgress(ImportPhase.Reading, discovered, total, 0, 0), force: true);
        }

        public void FileProcessed(bool failed)
        {
            var processed = Interlocked.Increment(ref _processed);
            var failedCount = failed ? Interlocked.Increment(ref _failed) : Volatile.Read(ref _failed);
            Report(new ImportProgress(ImportPhase.Reading, _discovered, _total, processed, failedCount), force: processed == _total);
        }

        public void Report(ImportProgress value, bool force)
        {
            if (progress is null)
            {
                return;
            }

            // Reported under the lock, and never older than what was already reported: otherwise a slower thread's
            // "24 / 25" could arrive after the final "25 / 25".
            lock (_gate)
            {
                if (value.Phase == ImportPhase.Reading && value.Processed < _lastProcessed)
                {
                    return;
                }

                var now = timeProvider.GetTimestamp();
                if (!force && _lastReport != long.MinValue && timeProvider.GetElapsedTime(_lastReport, now) < interval)
                {
                    return;
                }

                _lastReport = now;
                if (value.Phase == ImportPhase.Reading)
                {
                    _lastProcessed = value.Processed;
                }

                progress.Report(value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Import started: {FolderPath} (include subfolders: {IncludeSubfolders})")]
    private partial void LogStarting(string folderPath, bool includeSubfolders);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Folder could not be read and was skipped: {FolderPath}")]
    private partial void LogFolderSkipped(string folderPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Photo could not be read: {FilePath}")]
    private partial void LogFileFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Import finished: {FolderPath} cancelled={Cancelled} discovered={Discovered} added={Added} updated={Updated} unchanged={Unchanged} failed={Failed} missing={Missing} in {Seconds:0.0}s")]
    private partial void LogFinished(string folderPath, bool cancelled, int discovered, int added, int updated, int unchanged, int failed, int missing, double seconds);
}
