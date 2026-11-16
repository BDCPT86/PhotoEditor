namespace FrameForge.Infrastructure;

/// <summary>Locations of app-owned data (database, logs). Never contains user photos.</summary>
public sealed class AppDataPaths
{
    public const string DatabaseFileName = "frameforge.db";

    public AppDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        DatabasePath = Path.Combine(RootDirectory, DatabaseFileName);
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    /// <summary>Default per-user location: <c>%LOCALAPPDATA%\FrameForge</c>.</summary>
    public static AppDataPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"));

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
