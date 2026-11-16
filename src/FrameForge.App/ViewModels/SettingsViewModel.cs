using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>Analysis resolution choices (longest side in pixels).</summary>
public enum AnalysisResolution
{
    Fast = 512,
    Standard = 1024,
    Detailed = 2048,
}

public sealed partial class SettingsViewModel(ISettingsService settings, AiModelsViewModel aiModels, ILogger<SettingsViewModel> logger) : PageViewModel
{
    public override string Title => "Settings";

    /// <summary>Optional AI models: status, licence, consent-gated download, import and removal.</summary>
    public AiModelsViewModel AiModels { get; } = aiModels;

    // AI (optional, off by default; each needs its model installed)
    [ObservableProperty]
    private bool _aiFacesEnabled;

    [ObservableProperty]
    private bool _aiScenesEnabled;

    [ObservableProperty]
    private string _aiFaceMinScoreText = string.Empty;

    [ObservableProperty]
    private string _aiFaceSoftBelowText = string.Empty;

    [ObservableProperty]
    private string _aiSceneSimilarityText = string.Empty;

    public string LogRetentionHint { get; } =
        $"Whole days, {AppSettings.MinLogRetentionDays}–{AppSettings.MaxLogRetentionDays}. Older log files are removed when FrameForge starts.";

    [ObservableProperty]
    private AppPage _startPage;

    [ObservableProperty]
    private string _logRetentionDaysText = string.Empty;

    [ObservableProperty]
    private bool _viewerAutoAdvance = true;

    // Analysis — computation (changing re-analyses on the next Analyze run)
    [ObservableProperty]
    private AnalysisResolution _analysisResolution = AnalysisResolution.Standard;

    // Analysis — interpretation (applied instantly from cached results)
    [ObservableProperty]
    private string _blurThresholdText = string.Empty;

    [ObservableProperty]
    private string _highlightClipText = string.Empty;

    [ObservableProperty]
    private string _shadowClipText = string.Empty;

    [ObservableProperty]
    private string _maxHashDistanceText = string.Empty;

    [ObservableProperty]
    private string _burstWindowText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasError;

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            StartPage = await settings.GetAsync(AppSettings.StartPage, cancellationToken).ConfigureAwait(true);
            var days = await settings.GetAsync(AppSettings.LogRetentionDays, cancellationToken).ConfigureAwait(true);
            LogRetentionDaysText = days.ToString(culture);
            ViewerAutoAdvance = await settings.GetAsync(AppSettings.ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);

            AnalysisResolution = (AnalysisResolution)await settings.GetAsync(AppSettings.AnalysisMaxPixelSize, cancellationToken).ConfigureAwait(true);
            BlurThresholdText = (await settings.GetAsync(AppSettings.AnalysisBlurThreshold, cancellationToken).ConfigureAwait(true)).ToString(culture);
            HighlightClipText = (await settings.GetAsync(AppSettings.AnalysisHighlightClipPercent, cancellationToken).ConfigureAwait(true)).ToString(culture);
            ShadowClipText = (await settings.GetAsync(AppSettings.AnalysisShadowClipPercent, cancellationToken).ConfigureAwait(true)).ToString(culture);
            MaxHashDistanceText = (await settings.GetAsync(AppSettings.AnalysisMaxHashDistance, cancellationToken).ConfigureAwait(true)).ToString(culture);
            BurstWindowText = (await settings.GetAsync(AppSettings.AnalysisBurstWindowSeconds, cancellationToken).ConfigureAwait(true)).ToString(culture);

            AiFacesEnabled = await settings.GetAsync(AppSettings.AiFacesEnabled, cancellationToken).ConfigureAwait(true);
            AiScenesEnabled = await settings.GetAsync(AppSettings.AiScenesEnabled, cancellationToken).ConfigureAwait(true);
            AiFaceMinScoreText = (await settings.GetAsync(AppSettings.AiFaceMinScore, cancellationToken).ConfigureAwait(true)).ToString(culture);
            AiFaceSoftBelowText = (await settings.GetAsync(AppSettings.AiFaceSoftBelow, cancellationToken).ConfigureAwait(true)).ToString(culture);
            AiSceneSimilarityText = (await settings.GetAsync(AppSettings.AiSceneSimilarityMin, cancellationToken).ConfigureAwait(true)).ToString(culture);
            AiModels.Refresh();
            SetStatus(null, isError: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            SetStatus("Settings could not be loaded. See the log for details.", isError: true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!TryInt(LogRetentionDaysText, AppSettings.LogRetentionDays, out var days))
        {
            SetStatus($"Log retention must be a whole number from {AppSettings.MinLogRetentionDays} to {AppSettings.MaxLogRetentionDays}.", isError: true);
            return;
        }

        if (!TryDouble(BlurThresholdText, AppSettings.AnalysisBlurThreshold, out var blur))
        {
            SetStatus("Blur threshold must be a number greater than 0.", isError: true);
            return;
        }

        if (!TryDouble(HighlightClipText, AppSettings.AnalysisHighlightClipPercent, out var highlights)
            || !TryDouble(ShadowClipText, AppSettings.AnalysisShadowClipPercent, out var shadows))
        {
            SetStatus("Clipping limits must be percentages from 0 to 100.", isError: true);
            return;
        }

        if (!TryInt(MaxHashDistanceText, AppSettings.AnalysisMaxHashDistance, out var distance))
        {
            SetStatus("Similarity distance must be a whole number from 0 to 32.", isError: true);
            return;
        }

        if (!TryInt(BurstWindowText, AppSettings.AnalysisBurstWindowSeconds, out var window))
        {
            SetStatus("Burst window must be a whole number of seconds from 0 (no limit) to 86400.", isError: true);
            return;
        }

        if (!TryDouble(AiFaceMinScoreText, AppSettings.AiFaceMinScore, out var faceScore)
            || !TryDouble(AiSceneSimilarityText, AppSettings.AiSceneSimilarityMin, out var sceneSimilarity))
        {
            SetStatus("AI face score and scene similarity must be numbers from 0.5 to 1.", isError: true);
            return;
        }

        if (!TryDouble(AiFaceSoftBelowText, AppSettings.AiFaceSoftBelow, out var faceSoft))
        {
            SetStatus("Soft-face threshold must be a number greater than 0.", isError: true);
            return;
        }

        try
        {
            await settings.SetAsync(AppSettings.AiFacesEnabled, AiFacesEnabled, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AiScenesEnabled, AiScenesEnabled, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AiFaceMinScore, faceScore, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AiFaceSoftBelow, faceSoft, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AiSceneSimilarityMin, sceneSimilarity, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.StartPage, StartPage, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.LogRetentionDays, days, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.ViewerAutoAdvance, ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisMaxPixelSize, (int)AnalysisResolution, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisBlurThreshold, blur, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisHighlightClipPercent, highlights, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisShadowClipPercent, shadows, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisMaxHashDistance, distance, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.AnalysisBurstWindowSeconds, window, cancellationToken).ConfigureAwait(true);
            SetStatus("Settings saved. Analysis thresholds apply when you return to the Library; start page and log retention apply at next start.", isError: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex);
            SetStatus("Settings could not be saved. See the log for details.", isError: true);
        }
    }

    private static bool TryInt(string text, SettingDefinition<int> setting, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && setting.IsValid(value);

    private static bool TryDouble(string text, SettingDefinition<double> setting, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && setting.IsValid(value);

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        HasError = isError;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading settings failed.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving settings failed.")]
    private partial void LogSaveFailed(Exception exception);
}
