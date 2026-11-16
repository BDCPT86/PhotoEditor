using System.Reflection;
using FrameForge.Infrastructure;

namespace FrameForge.App.Startup;

/// <summary>Read-only facts about this installation, shown on the About page.</summary>
public sealed record AppInfo(string ProductName, string Version, string DataDirectory, string LogsDirectory, string DatabasePath)
{
    public static string CurrentVersion { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static AppInfo Create(AppDataPaths paths) =>
        new("FrameForge AI", CurrentVersion, paths.RootDirectory, paths.LogsDirectory, paths.DatabasePath);
}
