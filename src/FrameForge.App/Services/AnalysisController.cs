using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.Analysis;
using FrameForge.App.ViewModels;
using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;

namespace FrameForge.App.Services;

/// <summary>
/// Keeps tiles' <see cref="PhotoTileViewModel.Insight"/> (deterministic) and <see cref="PhotoTileViewModel.Ai"/>
/// (optional AI) in step with cached results and the user's thresholds. Threshold changes rebuild insights in memory
/// (no re-analysis); computation settings changes reload from the cache. AI is optional: when no model is installed or
/// a capability is off, AI insights are simply empty. Nothing here writes culling decisions.
/// UI thread only; heavy work runs on the thread pool.
/// </summary>
public sealed partial class AnalysisController(
    IPhotoAnalysisService service,
    IAiAnalysisService aiService,
    IAiRuntime aiRuntime,
    ISettingsService settings) : ObservableObject
{
    private IReadOnlyList<PhotoTileViewModel> _tiles = [];
    private IReadOnlyDictionary<Guid, PhotoAnalysis> _analyses = new Dictionary<Guid, PhotoAnalysis>();
    private IReadOnlyDictionary<Guid, AiPhotoAnalysis> _ai = new Dictionary<Guid, AiPhotoAnalysis>();

    /// <summary>Raised after insights were reapplied to tiles.</summary>
    public event EventHandler? InsightsChanged;

    public AnalysisSettings Settings { get; private set; } = AnalysisSettings.Default;

    public AnalysisThresholds Thresholds { get; private set; } = AnalysisThresholds.Default;

    public AiCapabilities AiCapabilities { get; private set; } = AiCapabilities.None;

    public AiThresholds AiThresholds { get; private set; } = AiThresholds.Default;

    [ObservableProperty]
    private int _analysedCount;

    public int GroupCount { get; private set; }

    /// <summary>Why the last AI step did nothing (model not installed, failed to load), or null.</summary>
    public string? AiUnavailableReason { get; private set; }

    /// <summary>Attaches to a (re)loaded library and applies cached results.</summary>
    public async Task LoadAsync(IReadOnlyList<PhotoTileViewModel> tiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = tiles;
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        await ReloadFromCacheAsync(cancellationToken).ConfigureAwait(true);
        await ApplyInsightsAsync().ConfigureAwait(true);
    }

    /// <summary>Re-reads settings; reloads from cache only if computation settings or AI capabilities changed.</summary>
    public async Task RefreshSettingsAsync(CancellationToken cancellationToken)
    {
        var previous = (Settings, AiCapabilities);
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        if (previous != (Settings, AiCapabilities))
        {
            aiRuntime.Reset();
            await ReloadFromCacheAsync(cancellationToken).ConfigureAwait(true);
        }

        await ApplyInsightsAsync().ConfigureAwait(true);
    }

    /// <summary>Deterministic analysis, then (if enabled and installed) AI. Returns both summaries.</summary>
    public async Task<(AnalysisRunSummary Deterministic, AnalysisRunSummary? Ai)> RunAsync(
        IProgress<AnalysisProgress>? progress,
        IProgress<AnalysisProgress>? aiProgress,
        CancellationToken cancellationToken)
    {
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        var photos = Photos();
        var summary = await service.AnalyzeAsync(photos, Settings, progress, cancellationToken).ConfigureAwait(true);

        AnalysisRunSummary? aiSummary = null;
        AiUnavailableReason = null;
        if (AiCapabilities.Any && !summary.WasCancelled)
        {
            aiSummary = await aiService.AnalyzeAsync(photos, AiCapabilities, Settings, aiProgress, cancellationToken).ConfigureAwait(true);
            // The service reports every photo as not eligible when no enabled model could be loaded.
            if (photos.Any(p => p.Status == Core.Photos.ImportStatus.Imported) && aiSummary.NotEligible == photos.Count)
            {
                AiUnavailableReason = aiRuntime.LastError ?? "the enabled AI models are not installed (Settings → AI models)";
            }
        }

        await ReloadFromCacheAsync(CancellationToken.None).ConfigureAwait(true);
        await ApplyInsightsAsync().ConfigureAwait(true);
        return (summary, aiSummary);
    }

    /// <summary>The focused photo's near-duplicate group, in library order (empty if not grouped).</summary>
    public IReadOnlyList<PhotoTileViewModel> GroupOf(PhotoTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        return tile.Insight.GroupKey is { } key ? _tiles.Where(t => t.Insight.GroupKey == key).ToList() : [];
    }

    private List<Core.Photos.Photo> Photos() => _tiles.Select(t => t.Photo).ToList();

    private async Task ReloadFromCacheAsync(CancellationToken cancellationToken)
    {
        var photos = Photos();
        _analyses = await service.LoadAsync(photos, Settings, cancellationToken).ConfigureAwait(true);
        _ai = await aiService.LoadAsync(photos, AiCapabilities, Settings, cancellationToken).ConfigureAwait(true);
    }

    private async Task ReadSettingsAsync(CancellationToken cancellationToken)
    {
        Settings = AnalysisSettings.Default with
        {
            MaxPixelSize = await settings.GetAsync(AppSettings.AnalysisMaxPixelSize, cancellationToken).ConfigureAwait(true),
        };
        var burst = await settings.GetAsync(AppSettings.AnalysisBurstWindowSeconds, cancellationToken).ConfigureAwait(true);
        Thresholds = new AnalysisThresholds
        {
            BlurScore = await settings.GetAsync(AppSettings.AnalysisBlurThreshold, cancellationToken).ConfigureAwait(true),
            HighlightClipPercent = await settings.GetAsync(AppSettings.AnalysisHighlightClipPercent, cancellationToken).ConfigureAwait(true),
            ShadowClipPercent = await settings.GetAsync(AppSettings.AnalysisShadowClipPercent, cancellationToken).ConfigureAwait(true),
            MaxHashDistance = await settings.GetAsync(AppSettings.AnalysisMaxHashDistance, cancellationToken).ConfigureAwait(true),
            BurstWindowSeconds = burst,
        };
        AiCapabilities = new AiCapabilities(
            await settings.GetAsync(AppSettings.AiFacesEnabled, cancellationToken).ConfigureAwait(true),
            await settings.GetAsync(AppSettings.AiScenesEnabled, cancellationToken).ConfigureAwait(true));
        AiThresholds = new AiThresholds
        {
            FaceMinScore = await settings.GetAsync(AppSettings.AiFaceMinScore, cancellationToken).ConfigureAwait(true),
            FaceSoftBelow = await settings.GetAsync(AppSettings.AiFaceSoftBelow, cancellationToken).ConfigureAwait(true),
            SceneSimilarityMin = await settings.GetAsync(AppSettings.AiSceneSimilarityMin, cancellationToken).ConfigureAwait(true),
            BurstWindowSeconds = burst,
        };
    }

    private async Task ApplyInsightsAsync()
    {
        var photos = Photos();
        var (analyses, ai, thresholds, aiThresholds, computeSettings) = (_analyses, _ai, Thresholds, AiThresholds, Settings);
        var (insights, aiInsights) = await Task.Run(() =>
        {
            var deterministic = AnalysisInsights.Build(photos, analyses, thresholds, computeSettings);
            return (deterministic, AiInsights.Build(photos, ai, analyses, deterministic, aiThresholds));
        }).ConfigureAwait(true);

        foreach (var tile in _tiles)
        {
            tile.Insight = insights.GetValueOrDefault(tile.Id) ?? PhotoInsight.NotAnalysed;
            tile.Ai = aiInsights.GetValueOrDefault(tile.Id) ?? AiInsight.None;
        }

        AnalysedCount = insights.Count;
        GroupCount = insights.Values.Where(i => i.GroupKey is not null).Select(i => i.GroupKey).Distinct().Count();
        InsightsChanged?.Invoke(this, EventArgs.Empty);
    }
}
