using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations to the local database.</summary>
public sealed partial class DatabaseMigrator(
    IDbContextFactory<FrameForgeDbContext> contextFactory,
    ILogger<DatabaseMigrator> logger)
{
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

                    LogApplying(pending.Count, pending);
                    await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                    LogApplied();
                }
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date.")]
    private partial void LogUpToDate();

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} database migration(s): {Migrations}")]
    private partial void LogApplying(int count, IReadOnlyList<string> migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrations applied.")]
    private partial void LogApplied();
}
