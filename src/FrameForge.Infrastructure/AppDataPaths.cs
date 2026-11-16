namespace FrameForge.Infrastructure;

/// <summary>Locations of app-owned data (database, logs, thumbnail cache). Never contains user photos.</summary>
public sealed class AppDataPaths
{
    public const string DatabaseFileName = "frameforge.db";

    public AppDataPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        DatabasePath = Path.Combine(RootDirectory, DatabaseFileName);
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        ThumbnailsDirectory = Path.Combine(RootDirectory, "thumbnails");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    /// <summary>Disposable cache; safe to delete at any time, thumbnails are regenerated on demand.</summary>
    public string ThumbnailsDirectory { get; }

    /// <summary>Default per-user location: <c>%LOCALAPPDATA%\FrameForge</c>.</summary>
    public static AppDataPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"));

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
    }
}
