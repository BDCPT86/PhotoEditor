using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.Analysis;
using FrameForge.App.ViewModels;
using FrameForge.Application.Analysis;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;

namespace FrameForge.App.Services;

/// <summary>
/// Keeps tiles' <see cref="PhotoTileViewModel.Insight"/> in step with cached measurements and the user's thresholds.
/// Threshold changes rebuild insights in memory (no re-analysis); computation settings changes reload from the cache,
/// where results for other settings are treated as stale. UI thread only; heavy work runs on the thread pool.
/// </summary>
public sealed partial class AnalysisController(IPhotoAnalysisService service, ISettingsService settings) : ObservableObject
{
    private IReadOnlyList<PhotoTileViewModel> _tiles = [];
    private IReadOnlyDictionary<Guid, PhotoAnalysis> _analyses = new Dictionary<Guid, PhotoAnalysis>();

    /// <summary>Raised after insights were reapplied to tiles.</summary>
    public event EventHandler? InsightsChanged;

    public AnalysisSettings Settings { get; private set; } = AnalysisSettings.Default;

    public AnalysisThresholds Thresholds { get; private set; } = AnalysisThresholds.Default;

    [ObservableProperty]
    private int _analysedCount;

    public int GroupCount { get; private set; }

    /// <summary>Attaches to a (re)loaded library and applies cached results.</summary>
    public async Task LoadAsync(IReadOnlyList<PhotoTileViewModel> tiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _tiles = tiles;
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        _analyses = await service.LoadAsync(Photos(), Settings, cancellationToken).ConfigureAwait(true);
        await ApplyInsightsAsync().ConfigureAwait(true);
    }

    /// <summary>Re-reads settings; reloads from cache only if computation settings changed.</summary>
    public async Task RefreshSettingsAsync(CancellationToken cancellationToken)
    {
        var previous = Settings;
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        if (previous != Settings)
        {
            _analyses = await service.LoadAsync(Photos(), Settings, cancellationToken).ConfigureAwait(true);
        }

        await ApplyInsightsAsync().ConfigureAwait(true);
    }

    public async Task<AnalysisRunSummary> RunAsync(IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        await ReadSettingsAsync(cancellationToken).ConfigureAwait(true);
        var summary = await service.AnalyzeAsync(Photos(), Settings, progress, cancellationToken).ConfigureAwait(true);
        _analyses = await service.LoadAsync(Photos(), Settings, CancellationToken.None).ConfigureAwait(true);
        await ApplyInsightsAsync().ConfigureAwait(true);
        return summary;
    }

    /// <summary>The focused photo's near-duplicate group, in library order (empty if not grouped).</summary>
    public IReadOnlyList<PhotoTileViewModel> GroupOf(PhotoTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        return tile.Insight.GroupKey is { } key ? _tiles.Where(t => t.Insight.GroupKey == key).ToList() : [];
    }

    private List<Core.Photos.Photo> Photos() => _tiles.Select(t => t.Photo).ToList();

    private async Task ReadSettingsAsync(CancellationToken cancellationToken)
    {
        Settings = AnalysisSettings.Default with
        {
            MaxPixelSize = await settings.GetAsync(AppSettings.AnalysisMaxPixelSize, cancellationToken).ConfigureAwait(true),
        };
        Thresholds = new AnalysisThresholds
        {
            BlurScore = await settings.GetAsync(AppSettings.AnalysisBlurThreshold, cancellationToken).ConfigureAwait(true),
            HighlightClipPercent = await settings.GetAsync(AppSettings.AnalysisHighlightClipPercent, cancellationToken).ConfigureAwait(true),
            ShadowClipPercent = await settings.GetAsync(AppSettings.AnalysisShadowClipPercent, cancellationToken).ConfigureAwait(true),
            MaxHashDistance = await settings.GetAsync(AppSettings.AnalysisMaxHashDistance, cancellationToken).ConfigureAwait(true),
            BurstWindowSeconds = await settings.GetAsync(AppSettings.AnalysisBurstWindowSeconds, cancellationToken).ConfigureAwait(true),
        };
    }

    private async Task ApplyInsightsAsync()
    {
        var photos = Photos();
        var analyses = _analyses;
        var thresholds = Thresholds;
        var computeSettings = Settings;
        var insights = await Task.Run(() => AnalysisInsights.Build(photos, analyses, thresholds, computeSettings)).ConfigureAwait(true);

        foreach (var tile in _tiles)
        {
            tile.Insight = insights.GetValueOrDefault(tile.Id) ?? PhotoInsight.NotAnalysed;
        }

        AnalysedCount = insights.Count;
        GroupCount = insights.Values.Where(i => i.GroupKey is not null).Select(i => i.GroupKey).Distinct().Count();
        InsightsChanged?.Invoke(this, EventArgs.Empty);
    }
}
