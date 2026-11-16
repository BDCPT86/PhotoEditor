using FrameForge.Application.Photos;
using FrameForge.Application.Settings;
using FrameForge.Infrastructure.Logging;
using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Photos;
using FrameForge.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FrameForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <remarks>Requires an <see cref="IThumbnailGenerator"/> registration (provided by FrameForge.Imaging).</remarks>
    public static IServiceCollection AddFrameForgeInfrastructure(this IServiceCollection services, AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ToString();

        services.AddSingleton(paths);
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<FrameForgeDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<LogFileRetention>();

        services.AddSingleton<IPhotoRepository, SqlitePhotoRepository>();
        services.AddSingleton<IPhotoFileEnumerator, FileSystemPhotoEnumerator>();
        services.AddSingleton<IThumbnailService, DiskThumbnailService>();

        return services;
    }
}
