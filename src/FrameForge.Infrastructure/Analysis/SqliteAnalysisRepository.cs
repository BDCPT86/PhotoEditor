using FrameForge.Application.Analysis;
using FrameForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrameForge.Infrastructure.Analysis;

public sealed class SqliteAnalysisRepository(IDbContextFactory<FrameForgeDbContext> contextFactory) : IAnalysisResultRepository
{
    // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs; run on the thread pool.

    public Task<IReadOnlyList<StoredAnalysis>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<StoredAnalysis>>(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    return await context.AnalysisResults
                        .AsNoTracking()
                        .Select(r => new StoredAnalysis(r.PhotoId, r.Kind, r.AlgorithmVersion, r.SettingsHash, r.SourceSizeBytes, r.SourceModifiedUtc, r.ValueJson, r.CreatedUtc))
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);

    public Task SaveAsync(IReadOnlyCollection<StoredAnalysis> results, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var latest = new Dictionary<(Guid, AnalysisKind), StoredAnalysis>();
                    foreach (var r in results)
                    {
                        latest[(r.PhotoId, r.Kind)] = r;
                    }

                    var ids = latest.Keys.Select(k => k.Item1).Distinct().ToList();
                    var existing = await context.AnalysisResults
                        .Where(r => ids.Contains(r.PhotoId))
                        .ToDictionaryAsync(r => (r.PhotoId, r.Kind), cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var (key, r) in latest)
                    {
                        if (!existing.TryGetValue(key, out var row))
                        {
                            row = new AnalysisResultEntity
                            {
                                PhotoId = r.PhotoId,
                                Kind = r.Kind,
                                AlgorithmVersion = r.AlgorithmVersion,
                                SettingsHash = r.SettingsHash,
                                ValueJson = r.ValueJson,
                            };
                            context.AnalysisResults.Add(row);
                        }

                        row.AlgorithmVersion = r.AlgorithmVersion;
                        row.SettingsHash = r.SettingsHash;
                        row.SourceSizeBytes = r.SourceSizeBytes;
                        row.SourceModifiedUtc = r.SourceModifiedUtc;
                        row.ValueJson = r.ValueJson;
                        row.CreatedUtc = r.CreatedUtc;
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}
