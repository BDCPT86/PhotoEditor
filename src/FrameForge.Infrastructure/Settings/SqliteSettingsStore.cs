using FrameForge.Application.Settings;
using FrameForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrameForge.Infrastructure.Settings;

public sealed class SqliteSettingsStore(
    IDbContextFactory<FrameForgeDbContext> contextFactory,
    TimeProvider timeProvider) : ISettingsStore
{
    // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs,
    // so each operation is moved to the thread pool to keep callers responsive.

    public Task<string?> GetValueJsonAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    return await context.AppSettings
                        .AsNoTracking()
                        .Where(s => s.Key == key)
                        .Select(s => s.ValueJson)
                        .SingleOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    public Task SetValueJsonAsync(string key, string valueJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(valueJson);

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    var entity = await context.AppSettings
                        .SingleOrDefaultAsync(s => s.Key == key, cancellationToken)
                        .ConfigureAwait(false);

                    if (entity is null)
                    {
                        context.AppSettings.Add(new AppSettingEntity { Key = key, ValueJson = valueJson, UpdatedUtc = now });
                    }
                    else
                    {
                        entity.ValueJson = valueJson;
                        entity.UpdatedUtc = now;
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}
