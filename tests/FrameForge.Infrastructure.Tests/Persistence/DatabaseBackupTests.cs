using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Persistence;

/// <summary>The copy made before an upgrade (ADR 0019).</summary>
public sealed class DatabaseBackupTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;

    public DatabaseBackupTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string BackupsFolder => Path.Combine(_paths.RootDirectory, DatabaseMigrator.BackupsFolderName);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private ServiceProvider Services(DateTimeOffset now) =>
        TestServices.Build(_paths, services => services.AddSingleton<TimeProvider>(new ManualTimeProvider(now)));

    /// <summary>A database as an older version left it: every migration but the last applied, with a row in it.</summary>
    private async Task CreateOlderDatabaseAsync()
    {
        await using var services = Services(DateTimeOffset.UnixEpoch);
        await using var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        var all = context.Database.GetMigrations().ToList();
        await context.GetService<IMigrator>().MigrateAsync(all[^2], Ct);
        await context.Database.ExecuteSqlRawAsync("INSERT INTO AppSettings (Key, ValueJson, UpdatedUtc) VALUES ('test.marker', '1', '2026-01-01')", Ct);
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ANewDatabase_IsNotBackedUp()
    {
        await using var services = Services(DateTimeOffset.UnixEpoch);

        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);

        Assert.False(Directory.Exists(BackupsFolder) && Directory.EnumerateFiles(BackupsFolder).Any());
    }

    [Fact]
    public async Task AnUpgrade_CopiesTheDatabaseFirst_WithItsData()
    {
        await CreateOlderDatabaseAsync();
        await using var services = Services(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));

        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        SqliteConnection.ClearAllPools();

        var backup = Assert.Single(Directory.EnumerateFiles(BackupsFolder));
        Assert.StartsWith("frameforge-20261125-120000-before-upgrade-from-", Path.GetFileName(backup), StringComparison.Ordinal);
        await using var copy = new SqliteConnection($"Data Source={backup};Pooling=False");
        await copy.OpenAsync(Ct);
        await using var query = copy.CreateCommand();
        query.CommandText = "SELECT ValueJson FROM AppSettings WHERE Key = 'test.marker'";
        Assert.Equal("1", await query.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task OnlyTheNewestThreeCopiesAreKept()
    {
        Directory.CreateDirectory(BackupsFolder);
        foreach (var day in new[] { "20260101", "20260201", "20260301" })
        {
            await File.WriteAllTextAsync(Path.Combine(BackupsFolder, $"frameforge-{day}-000000-before-upgrade-from-x.db"), "old", Ct);
        }

        await CreateOlderDatabaseAsync();
        await using var services = Services(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);

        var kept = Directory.EnumerateFiles(BackupsFolder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(DatabaseMigrator.BackupsKept, kept.Count);
        Assert.DoesNotContain(kept, n => n!.StartsWith("frameforge-20260101", StringComparison.Ordinal));
        Assert.Contains(kept, n => n!.StartsWith("frameforge-20261125", StringComparison.Ordinal));
    }
}
