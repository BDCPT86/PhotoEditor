using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure;

/// <summary>
/// Keeps the disposable caches (thumbnails and masks, see <see cref="AppDataPaths"/>) under a size limit. When they are
/// over it, the files written longest ago are deleted until the total is at 80 % of the limit, so trimming doesn't run
/// again straight away. Everything deleted is remade on demand. A file that can't be deleted (in use) is skipped.
/// </summary>
/// <remarks>
/// "Written longest ago" rather than "used longest ago": Windows doesn't reliably keep last-access times, and touching
/// each cached file when it is read would add a write per thumbnail shown.
/// </remarks>
public sealed partial class DiskCacheLimit(AppDataPaths paths, TimeProvider timeProvider, ILogger<DiskCacheLimit> logger)
{
    /// <summary>2 GB: about 100,000 thumbnails, or fewer with masks.</summary>
    public const long DefaultLimitBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Temporary files younger than this may still be being written, so they are left alone.</summary>
    private static readonly TimeSpan TempFileGrace = TimeSpan.FromHours(1);

    /// <returns>How big the caches were, how big they are now, and how many files were deleted.</returns>
    public CacheTrimResult Trim(long limitBytes = DefaultLimitBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var files = Files(paths.ThumbnailsDirectory).Concat(Files(paths.MasksDirectory))
            .Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || now - f.LastWriteTimeUtc > TempFileGrace)
            .ToList();
        var before = files.Sum(f => f.Length);
        if (before <= limitBytes)
        {
            return new CacheTrimResult(before, before, 0);
        }

        var target = limitBytes / 10 * 8;
        var total = before;
        var deleted = 0;
        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= target)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var size = file.Length; // read first: Delete() clears what FileInfo knows about the file
            try
            {
                file.Delete();
                total -= size;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSkipped(ex, file.Name);
            }
        }

        LogTrimmed(deleted, before / (1024 * 1024), total / (1024 * 1024), limitBytes / (1024 * 1024));
        return new CacheTrimResult(before, total, deleted);
    }

    /// <summary>Runs <see cref="Trim"/> on a worker thread; a failure is logged, never thrown (the caches only cost disk space).</summary>
    public Task TrimInBackgroundAsync() => Task.Run(() =>
    {
        try
        {
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFailed(ex);
        }
    });

    private static IEnumerable<FileInfo> Files(string directory) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            : [];

    [LoggerMessage(Level = LogLevel.Information, Message = "Cache trimmed: {Deleted} files removed, {BeforeMb} MB → {AfterMb} MB (limit {LimitMb} MB).")]
    private partial void LogTrimmed(int deleted, long beforeMb, long afterMb, long limitMb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trimming the caches failed; they are left as they are.")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cache file {FileName} could not be removed; left for next time.")]
    private partial void LogSkipped(Exception exception, string fileName);
}

/// <summary>Sizes in bytes.</summary>
public sealed record CacheTrimResult(long BytesBefore, long BytesAfter, int FilesDeleted);
