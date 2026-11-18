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
        ModelsDirectory = Path.Combine(RootDirectory, "models");
        MasksDirectory = Path.Combine(RootDirectory, "masks");
    }

    /// <summary>Disposable cache of computed masks (Milestone 08); safe to delete, masks are recomputed on demand.</summary>
    public string MasksDirectory { get; }

    /// <summary>Optional AI model files, installed only with the user's consent.</summary>
    public string ModelsDirectory { get; }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    /// <summary>Disposable cache; safe to delete at any time, thumbnails are regenerated on demand.</summary>
    public string ThumbnailsDirectory { get; }

    /// <summary>
    /// Development and testing only: when set, the app keeps its database, logs and thumbnail cache in this folder
    /// instead of the user's, so test runs never touch a real library. Not used in normal operation.
    /// </summary>
    public const string DataDirectoryOverrideVariable = "FRAMEFORGE_DATA_DIR";

    /// <summary>
    /// Per-user location <c>%LOCALAPPDATA%\FrameForge</c>, or the <see cref="DataDirectoryOverrideVariable"/> folder if set.
    /// </summary>
    public static AppDataPaths ForCurrentUser() =>
        Environment.GetEnvironmentVariable(DataDirectoryOverrideVariable) is { Length: > 0 } overridden
            ? new AppDataPaths(overridden)
            : new AppDataPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"));

    /// <summary>True when <see cref="DataDirectoryOverrideVariable"/> is in effect.</summary>
    public static bool IsOverridden => Environment.GetEnvironmentVariable(DataDirectoryOverrideVariable) is { Length: > 0 };

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
    }
}
