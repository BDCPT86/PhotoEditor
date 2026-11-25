using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations to the local database. Before changing an existing database (an upgrade), it
/// copies it to <c>backups\</c> with SQLite's online backup, keeping the newest <see cref="BackupsKept"/> copies. A new
/// database, or one that is up to date, is not copied. If the upgrade fails, <see cref="DatabaseUpgradeException"/>
/// says where the copy is (ADR 0019).
/// </summary>
public sealed partial class DatabaseMigrator(
    IDbContextFactory<FrameForgeDbContext> contextFactory,
    AppDataPaths paths,
    TimeProvider timeProvider,
    ILogger<DatabaseMigrator> logger)
{
    public const int BackupsKept = 3;
    public const string BackupsFolderName = "backups";

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs,
        // so run on the thread pool to keep the caller (often the UI thread) responsive.
        Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
                    if (pending.Count == 0)
                    {
                        LogUpToDate();
                        return;
                    }

                    var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
                    var backup = applied.Count > 0 && File.Exists(paths.DatabasePath) ? Backup(applied[^1]) : null;

                    LogApplying(pending.Count, pending);
                    try
                    {
                        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (backup is not null && ex is SqliteException or InvalidOperationException or DbUpdateException)
                    {
                        LogUpgradeFailed(ex, backup);
                        throw new DatabaseUpgradeException(backup, ex);
                    }

                    LogApplied();
                }
            },
            cancellationToken);

    /// <summary>Copies the database (consistently, even with a WAL journal) and removes all but the newest copies.</summary>
    private string Backup(string lastApplied)
    {
        var folder = Path.Combine(paths.RootDirectory, BackupsFolderName);
        Directory.CreateDirectory(folder);
        var stamp = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"frameforge-{stamp}-before-upgrade-from-{lastApplied}.db");

        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            source.Open();
            copy.Open();
            source.BackupDatabase(copy);
        }

        SqliteConnection.ClearAllPools();
        LogBackedUp(target);

        foreach (var old in new DirectoryInfo(folder).EnumerateFiles("frameforge-*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(BackupsKept))
        {
            try
            {
                old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogOldBackupKept(ex, old.Name);
            }
        }

        return target;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date.")]
    private partial void LogUpToDate();

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} database migration(s): {Migrations}")]
    private partial void LogApplying(int count, IReadOnlyList<string> migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied.")]
    private partial void LogApplied();

    [LoggerMessage(Level = LogLevel.Information, Message = "Database backed up before the upgrade to {Backup}")]
    private partial void LogBackedUp(string backup);

    [LoggerMessage(Level = LogLevel.Error, Message = "Database upgrade failed; the copy made before it is {Backup}")]
    private partial void LogUpgradeFailed(Exception exception, string backup);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Old database backup {Name} could not be removed.")]
    private partial void LogOldBackupKept(Exception exception, string name);
}

/// <summary>The database could not be upgraded. <see cref="BackupPath"/> is the copy made just before.</summary>
public sealed class DatabaseUpgradeException(string backupPath, Exception inner)
    : Exception($"The library database could not be upgraded. A copy made just before is at {backupPath}.", inner)
{
    public string BackupPath { get; } = backupPath;
}
