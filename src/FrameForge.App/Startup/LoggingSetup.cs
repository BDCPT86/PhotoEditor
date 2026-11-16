using System.Globalization;
using System.IO;
using FrameForge.Infrastructure;
using FrameForge.Infrastructure.Logging;
using Serilog;
using Serilog.Events;

namespace FrameForge.App.Startup;

internal static class LoggingSetup
{
    private const long MaxLogFileBytes = 50L * 1024 * 1024;

    public static ILogger CreateLogger(AppDataPaths paths) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .Enrich.FromLogContext()
            // Fallback for static Log.* calls; typed ILogger<T> loggers supply their own SourceContext.
            .Enrich.WithProperty("SourceContext", "FrameForge.App")
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, LogFileRetention.LogFileNameTemplate),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                // Age-based cleanup is done by LogFileRetention using the user's setting.
                retainedFileCountLimit: null,
                fileSizeLimitBytes: MaxLogFileBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
}
