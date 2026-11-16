using FrameForge.Application.Decisions;
using FrameForge.Core.Decisions;
using FrameForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrameForge.Infrastructure.Decisions;

public sealed class SqliteDecisionRepository(
    IDbContextFactory<FrameForgeDbContext> contextFactory,
    TimeProvider timeProvider) : IPhotoDecisionRepository
{
    // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs; run on the thread pool.

    public Task<IReadOnlyDictionary<Guid, PhotoDecision>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyDictionary<Guid, PhotoDecision>>(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var rows = await context.PhotoDecisions.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                    return rows.ToDictionary(r => r.PhotoId, r => new PhotoDecision(r.State, r.Rating, r.Label));
                }
            },
            cancellationToken);

    public Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> decisions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        if (decisions.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    // Last value wins if the same photo appears twice.
                    var latest = new Dictionary<Guid, PhotoDecision>();
                    foreach (var (id, decision) in decisions)
                    {
                        latest[id] = decision;
                    }

                    var ids = latest.Keys.ToList();
                    var existing = await context.PhotoDecisions
                        .Where(d => ids.Contains(d.PhotoId))
                        .ToDictionaryAsync(d => d.PhotoId, cancellationToken)
                        .ConfigureAwait(false);

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var (id, decision) in latest)
                    {
                        existing.TryGetValue(id, out var row);
                        if (decision.IsEmpty)
                        {
                            if (row is not null)
                            {
                                context.PhotoDecisions.Remove(row);
                            }

                            continue;
                        }

                        if (row is null)
                        {
                            row = new PhotoDecisionEntity { PhotoId = id };
                            context.PhotoDecisions.Add(row);
                        }

                        row.State = decision.State;
                        row.Rating = decision.Rating;
                        row.Label = decision.Label;
                        row.UpdatedUtc = now;
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}
