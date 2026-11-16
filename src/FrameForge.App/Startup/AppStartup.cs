using FrameForge.Application.Settings;
using FrameForge.App.ViewModels;
using FrameForge.Core.Settings;
using FrameForge.Infrastructure;
using FrameForge.Infrastructure.Logging;
using FrameForge.Infrastructure.Persistence;

namespace FrameForge.App.Startup;

/// <summary>Startup work that must finish before the shell is usable. Runs I/O off the UI thread.</summary>
internal sealed class AppStartup(
    DatabaseMigrator migrator,
    ISettingsService settings,
    LogFileRetention logRetention,
    AppDataPaths paths,
    ShellViewModel shell)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        shell.StatusMessage = "Preparing database…";
        await migrator.MigrateAsync(cancellationToken).ConfigureAwait(true);

        var retentionDays = await settings.GetAsync(AppSettings.LogRetentionDays, cancellationToken).ConfigureAwait(true);
        await Task.Run(() => logRetention.DeleteExpiredLogs(paths.LogsDirectory, retentionDays), cancellationToken).ConfigureAwait(true);

        await shell.InitializeAsync(cancellationToken).ConfigureAwait(true);
    }
}
