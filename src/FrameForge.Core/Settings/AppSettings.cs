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

    /// <summary>How many days of log files are kept; older files are removed at startup.</summary>
    public static readonly SettingDefinition<int> LogRetentionDays =
        new("logging.retentionDays", DefaultLogRetentionDays, days => days is >= MinLogRetentionDays and <= MaxLogRetentionDays);
}
