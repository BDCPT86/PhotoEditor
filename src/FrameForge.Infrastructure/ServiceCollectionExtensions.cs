using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Infrastructure.Ai;
using FrameForge.Application.Decisions;
using FrameForge.Application.Editing;
using FrameForge.Infrastructure.Editing;
using FrameForge.Infrastructure.Analysis;
using FrameForge.Application.Export;
using FrameForge.Application.Photos;
using FrameForge.Infrastructure.Decisions;
using FrameForge.Infrastructure.Export;
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
        services.AddSingleton<DiskCacheLimit>();
        services.AddSingleton<Diagnostics.FileCrashReports>();
        services.AddSingleton<Application.Diagnostics.ICrashReports>(sp => sp.GetRequiredService<Diagnostics.FileCrashReports>());

        services.AddSingleton<IPhotoRepository, SqlitePhotoRepository>();
        services.AddSingleton<IPhotoFileEnumerator, FileSystemPhotoEnumerator>();
        services.AddSingleton<IFolderProbe, FileSystemFolderProbe>();
        services.AddSingleton<IThumbnailService, DiskThumbnailService>();

        services.AddSingleton<IPhotoDecisionRepository, SqliteDecisionRepository>();
        services.AddSingleton<IExportService, FileSystemExportService>();

        // M09: the only way FrameForge removes a photo. Windows only, like the rest of the app.
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<Cleanup.IRecycleBinPlatform, Cleanup.WindowsRecycleBinPlatform>();
            services.AddSingleton<Application.Cleanup.IRecycleBin, Cleanup.RecycleBinService>();
        }

        services.AddSingleton<IAnalysisResultRepository, SqliteAnalysisRepository>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();

        services.AddSingleton<IEditRepository, SqliteEditRepository>();
        services.AddSingleton<IPresetRepository, SqlitePresetRepository>();
        services.AddSingleton<IEditedExportService, FileSystemEditedExportService>();
        services.AddSingleton<Application.Editing.Masks.IMaskCache, DiskMaskCache>();

        // Used only for user-initiated model downloads. Generic agent; no user data is sent.
        services.AddSingleton<IModelStore>(sp => new FileSystemModelStore(
            sp.GetRequiredService<AppDataPaths>(),
            new HttpClient { Timeout = TimeSpan.FromMinutes(30), DefaultRequestHeaders = { { "User-Agent", "FrameForge-ModelDownloader/1.0" } } },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileSystemModelStore>>()));

        return services;
    }
}
