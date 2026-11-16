using System.IO;
using System.Windows;
using System.Windows.Threading;
using FrameForge.App.Startup;
using FrameForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FrameForge.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

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
        RegisterGlobalExceptionHandlers();
        Log.Information("FrameForge {Version} starting on {OS} (.NET {Runtime})",
            AppInfo.CurrentVersion, Environment.OSVersion.VersionString, Environment.Version);

        try
        {
            _host = CompositionRoot.BuildHost(e.Args, paths);
            await _host.StartAsync().ConfigureAwait(true);

            // Show the shell immediately; startup work runs off the UI thread while it displays progress.
            _host.Services.GetRequiredService<MainWindow>().Show();
            await _host.Services.GetRequiredService<AppStartup>().RunAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            ShowFatalError("FrameForge could not start. See the log for details:\n" + paths.LogsDirectory);
            Shutdown(1);
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
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
        MessageBox.Show(
            "An unexpected error occurred. Details were written to the log.\n\n" + e.Exception.Message,
            "FrameForge",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep the app alive for recoverable UI errors; the error is logged, not swallowed.
        e.Handled = true;
    }

    private static void ShowFatalError(string message) =>
        MessageBox.Show(message, "FrameForge", MessageBoxButton.OK, MessageBoxImage.Error);
}
