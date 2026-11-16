namespace FrameForge.Core.Settings;

/// <summary>The application's known settings and their defaults.</summary>
public static class AppSettings
{
    public const int MinLogRetentionDays = 1;
    public const int MaxLogRetentionDays = 365;
    public const int DefaultLogRetentionDays = 14;

    /// <summary>Page shown when the application starts.</summary>
    public static readonly SettingDefinition<AppPage> StartPage =
        new("app.startPage", AppPage.Library, page => Enum.IsDefined(page));

    /// <summary>Whether folder import also scans sub-folders. Remembered from the last import.</summary>
    public static readonly SettingDefinition<bool> ImportIncludeSubfolders =
        new("import.includeSubfolders", true);

    /// <summary>Folder the import picker opens in. Empty until the first import.</summary>
    public static readonly SettingDefinition<string> ImportLastFolder =
        new("import.lastFolder", string.Empty);

    /// <summary>How many days of log files are kept; older files are removed at startup.</summary>
    public static readonly SettingDefinition<int> LogRetentionDays =
        new("logging.retentionDays", DefaultLogRetentionDays, days => days is >= MinLogRetentionDays and <= MaxLogRetentionDays);
}
