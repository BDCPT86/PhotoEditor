using FrameForge.Application;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;
using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Persistence;

/// <summary>Exercises the real SQLite database file through the production DI registrations.</summary>
public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;

    public SettingsPersistenceTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private ServiceProvider BuildServices(TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection().AddLogging();
        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        return services
            .AddFrameForgeApplication()
            .AddFrameForgeInfrastructure(_paths)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Migrate_CreatesDatabase_AndModelMatchesMigrations()
    {
        await using var services = BuildServices();

        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);

        Assert.True(File.Exists(_paths.DatabasePath));
        await using var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Ct));
        Assert.False(context.Database.HasPendingModelChanges(), "The EF model has changed without a migration. Run `dotnet ef migrations add`.");
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        await using var services = BuildServices();
        var migrator = services.GetRequiredService<DatabaseMigrator>();

        await migrator.MigrateAsync(Ct);
        await migrator.MigrateAsync(Ct);
    }

    [Fact]
    public async Task Settings_PersistAndCanBeReadBack_AfterRestart()
    {
        await using (var firstRun = BuildServices())
        {
            await firstRun.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
            var settings = firstRun.GetRequiredService<ISettingsService>();
            await settings.SetAsync(AppSettings.StartPage, AppPage.About, Ct);
            await settings.SetAsync(AppSettings.LogRetentionDays, 45, Ct);
        }

        await using var secondRun = BuildServices();
        await secondRun.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        var reloaded = secondRun.GetRequiredService<ISettingsService>();

        Assert.Equal(AppPage.About, await reloaded.GetAsync(AppSettings.StartPage, Ct));
        Assert.Equal(45, await reloaded.GetAsync(AppSettings.LogRetentionDays, Ct));
    }

    [Fact]
    public async Task SetValue_OverwritesExistingRow_AndRecordsUtcTimestamp()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        await using var services = BuildServices(clock);
        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        var store = services.GetRequiredService<ISettingsStore>();

        await store.SetValueJsonAsync("test.key", "1", Ct);
        clock.UtcNow = clock.UtcNow.AddHours(1);
        await store.SetValueJsonAsync("test.key", "2", Ct);

        await using var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        var row = Assert.Single(await context.AppSettings.ToListAsync(Ct));
        Assert.Equal("2", row.ValueJson);
        Assert.Equal(clock.UtcNow.UtcDateTime, row.UpdatedUtc);
        Assert.Equal(DateTimeKind.Utc, row.UpdatedUtc.Kind);
    }

    [Fact]
    public async Task GetValue_ReturnsNull_ForUnknownKey()
    {
        await using var services = BuildServices();
        await services.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);

        Assert.Null(await services.GetRequiredService<ISettingsStore>().GetValueJsonAsync("missing.key", Ct));
    }
}
