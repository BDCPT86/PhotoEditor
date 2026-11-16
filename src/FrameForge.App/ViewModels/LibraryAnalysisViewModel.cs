using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Analysis;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>Analysis run (progress, cancel) and the "why" details panel for the Library page.</summary>
public sealed partial class LibraryAnalysisViewModel(AnalysisController controller, ILogger<LibraryAnalysisViewModel> logger) : ObservableObject
{
    public string HeuristicNote { get; } = AnalysisInsights.HeuristicNote;

    public AnalysisController Controller { get; } = controller;

    /// <summary>Raised with a user-facing summary when a run ends (message, isWarning).</summary>
    public event EventHandler<(string Message, bool IsWarning)>? RunFinished;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private bool _isDetailsOpen;

    /// <summary>The photo the details panel describes (grid selection, viewer photo, or compare panel).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details), nameof(DetailsTitle), nameof(HasDetails))]
    private PhotoTileViewModel? _detailsTile;

    public IReadOnlyList<InsightLine> Details => DetailsTile is null ? [] : [.. DetailsTile.Insight.Details, .. DetailsTile.Ai.Details];

    public bool HasDetails => Details.Count > 0;

    public string DetailsTitle => DetailsTile is null
        ? "No photo selected"
        : DetailsTile.Insight.IsAnalysed ? DetailsTile.FileName : DetailsTile.FileName + " — not analysed yet (press Analyze)";

    /// <summary>Re-reads the current tile's insight (after thresholds or results change).</summary>
    public void RefreshDetails()
    {
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(DetailsTitle));
        OnPropertyChanged(nameof(HasDetails));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task AnalyzeAsync(CancellationToken cancellationToken)
    {
        IsAnalyzing = true;
        ProgressValue = 0;
        ProgressText = "Preparing…";
        try
        {
            var (summary, ai) = await Controller.RunAsync(
                new Progress<AnalysisProgress>(p => OnProgress(p, "Analysing")),
                new Progress<AnalysisProgress>(p => OnProgress(p, "AI analysis")),
                cancellationToken).ConfigureAwait(true);
            var message = Describe(summary, Controller.GroupCount) + DescribeAi(ai, Controller.AiUnavailableReason);
            RunFinished?.Invoke(this, (message, summary.Failed > 0 || summary.WasCancelled || ai?.WasCancelled == true || Controller.AiUnavailableReason is not null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRunFailed(ex);
            RunFinished?.Invoke(this, ("Analysis stopped because of an unexpected error. Results so far were kept. See the log for details.", true));
        }
        finally
        {
            IsAnalyzing = false;
            ProgressText = null;
            RefreshDetails();
        }
    }

    private void OnProgress(AnalysisProgress progress, string phase)
    {
        var culture = CultureInfo.CurrentCulture;
        ProgressValue = progress.Total == 0 ? 1 : (double)progress.Processed / progress.Total;
        ProgressText = progress.Total == 0
            ? phase + ": all photos already up to date"
            : string.Create(culture, $"{phase} {progress.Processed:N0} / {progress.Total:N0}")
                + (progress.Failed > 0 ? string.Create(culture, $"  ·  {progress.Failed:N0} unreadable") : string.Empty);
    }

    internal static string DescribeAi(AnalysisRunSummary? ai, string? unavailableReason)
    {
        if (ai is null)
        {
            return string.Empty;
        }

        if (unavailableReason is not null)
        {
            return " AI analysis skipped: " + unavailableReason + ".";
        }

        var culture = CultureInfo.CurrentCulture;
        return string.Create(culture, $" AI: {ai.Analysed:N0} analysed, {ai.Cached:N0} up to date")
            + (ai.Failed > 0 ? string.Create(culture, $", {ai.Failed:N0} failed") : string.Empty)
            + (ai.WasCancelled ? " (cancelled)." : ".");
    }

    internal static string Describe(AnalysisRunSummary summary, int groupCount)
    {
        var culture = CultureInfo.CurrentCulture;
        var head = summary.WasCancelled ? "Analysis cancelled." : "Analysis finished.";
        var text = string.Create(culture, $"{head} {summary.Analysed:N0} analysed, {summary.Cached:N0} already up to date")
            + (summary.Failed > 0 ? string.Create(culture, $", {summary.Failed:N0} unreadable") : string.Empty)
            + string.Create(culture, $". {groupCount:N0} similar group(s) found.");
        return summary.WasCancelled ? text + " Run it again to continue." : text;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis run failed.")]
    private partial void LogRunFailed(Exception exception);
}
