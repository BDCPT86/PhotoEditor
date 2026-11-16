using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Logging;

/// <summary>
/// Deletes the application's own log files once they are older than the configured retention.
/// Only files matching <see cref="LogFileSearchPattern"/> in the logs directory are considered.
/// </summary>
public sealed partial class LogFileRetention(TimeProvider timeProvider, ILogger<LogFileRetention> logger)
{
    /// <summary>File name template passed to the rolling file sink (a date is inserted before the extension).</summary>
    public const string LogFileNameTemplate = "frameforge-.log";

    public const string LogFileSearchPattern = "frameforge-*.log";

    /// <returns>The number of files deleted.</returns>
    public int DeleteExpiredLogs(string logsDirectory, int retentionDays)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);

        if (!Directory.Exists(logsDirectory))
        {
            return 0;
        }

        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;

        foreach (var file in new DirectoryInfo(logsDirectory).EnumerateFiles(LogFileSearchPattern, SearchOption.TopDirectoryOnly))
        {
            if (file.LastWriteTimeUtc >= cutoffUtc)
            {
                continue;
            }

            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogDeleteFailed(ex, file.Name);
            }
        }

        if (deleted > 0)
        {
            LogDeleted(deleted, retentionDays);
        }

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete expired log file {LogFileName}.")]
    private partial void LogDeleteFailed(Exception exception, string logFileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} log file(s) older than {RetentionDays} day(s).")]
    private partial void LogDeleted(int count, int retentionDays);
}
