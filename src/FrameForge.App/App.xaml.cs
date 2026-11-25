using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameForge.App.Startup;
using FrameForge.Infrastructure;
using FrameForge.Infrastructure.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FrameForge.App;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Development and testing only: with the data folder overridden, setting this to 1 crashes the app on a worker
    /// thread a few seconds after start-up, to exercise the crash report (ADR 0019).
    /// </summary>
    internal const string CrashTestVariable = "FRAMEFORGE_CRASH_TEST";

    private IHost? _host;
    private FileCrashReports? _crashReports;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppDataPaths.ForCurrentUser();
        try
        {
            paths.EnsureDirectoriesExist();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowFatalError($"FrameForge could not create its data folder:\n{paths.RootDirectory}\n\n{ex.Message}");
            Shutdown(1);
            return;
        }

        Log.Logger = LoggingSetup.CreateLogger(paths);
        _crashReports = new FileCrashReports(paths, TimeProvider.System);
        RegisterGlobalExceptionHandlers();
        Log.Information("FrameForge {Version} starting on {OS} (.NET {Runtime})",
            AppInfo.CurrentVersion, Environment.OSVersion.VersionString, Environment.Version);
        if (AppDataPaths.IsOverridden)
        {
            Log.Warning("Using data folder override {Variable}={DataDirectory} (development/testing)",
                AppDataPaths.DataDirectoryOverrideVariable, paths.RootDirectory);
        }

        try
        {
            _host = CompositionRoot.BuildHost(e.Args, paths);
            await _host.StartAsync().ConfigureAwait(true);

            // Show the shell immediately; startup work runs off the UI thread while it displays progress.
            _host.Services.GetRequiredService<MainWindow>().Show();
            await _host.Services.GetRequiredService<AppStartup>().RunAsync(CancellationToken.None).ConfigureAwait(true);
            StartCrashTestIfAsked();
        }
        catch (FrameForge.Infrastructure.Persistence.DatabaseUpgradeException ex)
        {
            Log.Fatal(ex, "Database upgrade failed");
            _crashReports.Write(ex, "The library database could not be upgraded; FrameForge did not start");
            ShowFatalError("FrameForge could not upgrade your library database to this version.\n\n"
                + "A copy made just before the upgrade is here:\n" + ex.BackupPath + "\n\n"
                + "Your photos are not affected. See the log for details:\n" + paths.LogsDirectory);
            Shutdown(1);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            _crashReports.Write(ex, "FrameForge could not start");
            ShowFatalError("FrameForge could not start. A report was saved in:\n" + paths.CrashReportsDirectory
                + "\n\nSee the log for details:\n" + paths.LogsDirectory);
            Shutdown(1);
        }
    }

    private static void StartCrashTestIfAsked()
    {
        if (AppDataPaths.IsOverridden && Environment.GetEnvironmentVariable(CrashTestVariable) == "1")
        {
            Log.Warning("Crash test requested ({Variable}=1): crashing in 3 s", CrashTestVariable);
            new Thread(() =>
            {
                Thread.Sleep(3000);
                throw new InvalidOperationException("Crash test (" + CrashTestVariable + "=1)");
            }) { IsBackground = true }.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("FrameForge exiting with code {ExitCode}", e.ApplicationExitCode);
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown error: " + args.ExceptionObject);
            Log.Fatal(exception, "Unhandled exception (terminating: {IsTerminating})", args.IsTerminating);
            Log.CloseAndFlush(); // so the report includes these last lines
            _crashReports?.Write(exception, args.IsTerminating ? "Unhandled error; FrameForge closed" : "Unhandled error");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        var saved = _crashReports?.Write(e.Exception, "Error on the UI thread; FrameForge kept running") is not null;
        MessageBox.Show(
            "An unexpected error occurred. " + (saved ? "A report was saved on this computer (nothing is sent)." : "Details were written to the log.")
            + "\n\n" + e.Exception.Message,
            "FrameForge",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep the app alive for recoverable UI errors; the error is logged, not swallowed.
        e.Handled = true;
    }

    private static void ShowFatalError(string message) =>
        MessageBox.Show(message, "FrameForge", MessageBoxButton.OK, MessageBoxImage.Error);
}
