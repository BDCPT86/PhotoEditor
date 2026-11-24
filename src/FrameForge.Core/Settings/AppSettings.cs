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

    /// <summary>In the viewer, move to the next photo after a Keep/Reject/Review/Unset decision.</summary>
    public static readonly SettingDefinition<bool> ViewerAutoAdvance =
        new("viewer.autoAdvance", true);

    /// <summary>Thumbnail size in the library grid.</summary>
    public static readonly SettingDefinition<GridDensity> LibraryGridDensity =
        new("library.gridDensity", GridDensity.Medium, density => Enum.IsDefined(density));

    // ---- Analysis: computation (changing it makes stored results stale) ----

    /// <summary>Longest side, in pixels, of the decoded image that metrics are computed on.</summary>
    public static readonly SettingDefinition<int> AnalysisMaxPixelSize =
        new("analysis.maxPixelSize", 1024, size => size is 512 or 1024 or 2048);

    // ---- Analysis: interpretation (applied instantly, no re-analysis) ----

    /// <summary>Sharpness score below which a photo is flagged "likely blurry". Heuristic; tune for your camera.</summary>
    public static readonly SettingDefinition<double> AnalysisBlurThreshold =
        new("analysis.blurThreshold", 60.0, v => v is > 0 and <= 100_000);

    /// <summary>Percent of pixels with a clipped channel above which highlights are flagged.</summary>
    public static readonly SettingDefinition<double> AnalysisHighlightClipPercent =
        new("analysis.highlightClipPercent", 1.0, v => v is >= 0 and <= 100);

    /// <summary>Percent of fully black pixels above which shadows are flagged.</summary>
    public static readonly SettingDefinition<double> AnalysisShadowClipPercent =
        new("analysis.shadowClipPercent", 5.0, v => v is >= 0 and <= 100);

    /// <summary>Maximum differing bits (of 64) for two photos to count as near-duplicates.</summary>
    public static readonly SettingDefinition<int> AnalysisMaxHashDistance =
        new("analysis.maxHashDistance", 10, v => v is >= 0 and <= 32);

    /// <summary>Near-duplicates must be captured within this many seconds of each other; 0 = no time limit.</summary>
    public static readonly SettingDefinition<int> AnalysisBurstWindowSeconds =
        new("analysis.burstWindowSeconds", 10, v => v is >= 0 and <= 86_400);

    // ---- AI (optional, advisory; off by default) ----

    /// <summary>Run the face detector during Analyze (requires the model to be installed).</summary>
    public static readonly SettingDefinition<bool> AiFacesEnabled = new("ai.faces.enabled", false);

    /// <summary>Compute scene embeddings during Analyze (requires the model to be installed).</summary>
    public static readonly SettingDefinition<bool> AiScenesEnabled = new("ai.scenes.enabled", false);

    /// <summary>Detector score at or above which a face counts (0.5–1).</summary>
    public static readonly SettingDefinition<double> AiFaceMinScore =
        new("ai.faceMinScore", 0.6, v => v is >= 0.5 and <= 1);

    /// <summary>A face region's sharpness score below which the face is "soft".</summary>
    public static readonly SettingDefinition<double> AiFaceSoftBelow =
        new("ai.faceSoftBelow", 40.0, v => v is > 0 and <= 100_000);

    /// <summary>Cosine similarity at or above which two photos count as the same scene (0.5–1).</summary>
    public static readonly SettingDefinition<double> AiSceneSimilarityMin =
        new("ai.sceneSimilarityMin", 0.9, v => v is >= 0.5 and <= 1);

    /// <summary>How many days of log files are kept; older files are removed at startup.</summary>
    public static readonly SettingDefinition<int> LogRetentionDays =
        new("logging.retentionDays", DefaultLogRetentionDays, days => days is >= MinLogRetentionDays and <= MaxLogRetentionDays);
}
