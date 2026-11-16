using FrameForge.Application.Editing;
using FrameForge.Core.Editing;
using FrameForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FrameForge.Infrastructure.Editing;

public sealed class SqliteEditRepository(IDbContextFactory<FrameForgeDbContext> contextFactory, TimeProvider timeProvider) : IEditRepository
{
    // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs; run on the thread pool.

    public Task<StoredEdits> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var rows = await context.PhotoEdits.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                    var recipes = new Dictionary<Guid, EditRecipe>();
                    var rejected = new List<(Guid, string)>();
                    foreach (var row in rows)
                    {
                        if (RecipeJson.TryDeserialize(row.RecipeJson, out var error) is { } recipe)
                        {
                            recipes[row.PhotoId] = recipe;
                        }
                        else
                        {
                            rejected.Add((row.PhotoId, error ?? "Unreadable recipe."));
                        }
                    }

                    return new StoredEdits(recipes, rejected);
                }
            },
            cancellationToken);

    public Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        if (recipes.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Validate everything before touching the database, so a bad recipe never half-saves a batch.
        var latest = new Dictionary<Guid, EditRecipe>();
        foreach (var (id, recipe) in recipes)
        {
            latest[id] = recipe.EnsureValid();
        }

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var ids = latest.Keys.ToList();
                    var existing = await context.PhotoEdits.Where(e => ids.Contains(e.PhotoId)).ToDictionaryAsync(e => e.PhotoId, cancellationToken).ConfigureAwait(false);
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var (id, recipe) in latest)
                    {
                        existing.TryGetValue(id, out var row);
                        if (recipe.IsNeutral)
                        {
                            if (row is not null)
                            {
                                context.PhotoEdits.Remove(row);
                            }

                            continue;
                        }

                        if (row is null)
                        {
                            row = new PhotoEditEntity { PhotoId = id };
                            context.PhotoEdits.Add(row);
                        }

                        row.SchemaVersion = recipe.SchemaVersion;
                        row.RecipeJson = RecipeJson.Serialize(recipe);
                        row.UpdatedUtc = now;
                    }

                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
    }
}

public sealed class SqlitePresetRepository(IDbContextFactory<FrameForgeDbContext> contextFactory, TimeProvider timeProvider) : IPresetRepository
{
    public Task<StoredPresets> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var rows = await context.EditPresets.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
                    var presets = new List<EditPreset>();
                    var rejected = new List<(Guid, string)>();
                    foreach (var row in rows)
                    {
                        var recipe = RecipeJson.TryDeserialize(row.RecipeJson, out var error);
                        if (recipe is null || !Enum.TryParse<PresetGroups>(row.Groups, out var groups))
                        {
                            rejected.Add((row.Id, error ?? $"Unknown preset groups '{row.Groups}'."));
                            continue;
                        }

                        var preset = new EditPreset(row.Id, row.Name, recipe, groups);
                        if (preset.Validate() is { Count: > 0 } problems)
                        {
                            rejected.Add((row.Id, string.Join(" ", problems)));
                            continue;
                        }

                        presets.Add(preset);
                    }

                    return new StoredPresets(presets, rejected);
                }
            },
            cancellationToken);

    public Task SaveAsync(EditPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Validate() is { Count: > 0 } problems)
        {
            throw new ArgumentException("Invalid preset: " + string.Join(" ", problems), nameof(preset));
        }

        return Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    var name = preset.Name.Trim();
                    var clash = await context.EditPresets.AnyAsync(p => p.Name == name && p.Id != preset.Id, cancellationToken).ConfigureAwait(false);
                    if (clash)
                    {
                        throw new DuplicatePresetNameException($"A preset called “{name}” already exists.");
                    }

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    var row = await context.EditPresets.FindAsync([preset.Id], cancellationToken).ConfigureAwait(false);
                    if (row is null)
                    {
                        row = new EditPresetEntity { Id = preset.Id, CreatedUtc = now };
                        context.EditPresets.Add(row);
                    }

                    row.Name = name;
                    row.Groups = preset.Groups.ToString();
                    row.SchemaVersion = preset.Recipe.SchemaVersion;
                    row.RecipeJson = RecipeJson.Serialize(preset.Recipe);
                    row.UpdatedUtc = now;
                    try
                    {
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
                    {
                        // A concurrent save took the name between the check and the insert.
                        throw new DuplicatePresetNameException($"A preset called “{name}” already exists.", ex);
                    }
                }
            },
            cancellationToken);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    await context.EditPresets.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                }
            },
            cancellationToken);
}
