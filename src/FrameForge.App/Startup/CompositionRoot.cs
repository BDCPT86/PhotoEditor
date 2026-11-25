using System.Windows.Threading;
using FrameForge.AI;
using FrameForge.Application;
using FrameForge.App.Services;
using FrameForge.App.ViewModels;
using FrameForge.Imaging;
using FrameForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FrameForge.App.Startup;

/// <summary>The single place where the application's object graph is composed.</summary>
internal static class CompositionRoot
{
    /// <remarks>Must be called on the UI thread (captures its dispatcher).</remarks>
    public static IHost BuildHost(string[] args, AppDataPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // A desktop app has no console; suppress the host's "Press Ctrl+C" status messages.
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.AddFrameForgeApplication();
        builder.Services.AddFrameForgeInfrastructure(paths);
        builder.Services.AddFrameForgeImaging();
        builder.Services.AddFrameForgeAi();

        builder.Services.AddSingleton(AppInfo.Create(paths));
        builder.Services.AddSingleton<AppStartup>();

        // UI services
        builder.Services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(Dispatcher.CurrentDispatcher));
        builder.Services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        builder.Services.AddSingleton<IFileSavePicker, WindowsFileSavePicker>();
        builder.Services.AddSingleton<IFileOpenPicker, WindowsFileOpenPicker>();
        builder.Services.AddSingleton<IDesktopShell, WindowsDesktopShell>();
        builder.Services.AddSingleton<CrashNoticeViewModel>();
        builder.Services.AddSingleton<ThumbnailLoader>();
        builder.Services.AddSingleton<IThumbnailSource>(sp => sp.GetRequiredService<ThumbnailLoader>());
        builder.Services.AddSingleton<DecisionController>();
        builder.Services.AddSingleton<AnalysisController>();
        builder.Services.AddSingleton<FrameForge.Application.Editing.Auto.IFaceLookup>(sp => sp.GetRequiredService<AnalysisController>());
        builder.Services.AddSingleton<EditController>();

        // View models and shell
        builder.Services.AddSingleton<LibraryFilterViewModel>();
        builder.Services.AddSingleton<MaskListViewModel>();
        builder.Services.AddSingleton<DevelopViewModel>();
        builder.Services.AddSingleton<ViewerViewModel>();
        builder.Services.AddSingleton<PresetPanelViewModel>();
        builder.Services.AddSingleton<CompareViewModel>();
        builder.Services.AddSingleton<ExportViewModel>();
        builder.Services.AddSingleton<LocateFolderViewModel>();
        builder.Services.AddSingleton<LibraryAnalysisViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<AiModelsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<AboutViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
