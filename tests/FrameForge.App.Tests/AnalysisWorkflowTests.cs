using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Settings;

namespace FrameForge.App.Tests;

public sealed class AnalysisWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>IMG_00–02 are near-duplicates taken a second apart; IMG_03 is unrelated; IMG_04–05 are similar to each other.</summary>
    private static CullingFixture Grouped()
    {
        var f = new CullingFixture();
        f.AnalysisService.Set(f.Photos[0], 0b0000, sharpness: 120);
        f.AnalysisService.Set(f.Photos[1], 0b0001, sharpness: 480);
        f.AnalysisService.Set(f.Photos[2], 0b0011, sharpness: 50);
        f.AnalysisService.Set(f.Photos[3], ulong.MaxValue);
        // 32 bits from both the first group and IMG_03: well outside the default limit of 10.
        f.AnalysisService.Set(f.Photos[4], 0xFFFF_FFFF_0000_0000);
        f.AnalysisService.Set(f.Photos[5], 0xFFFF_FFFF_0000_0001);
        return f;
    }

    [Fact]
    public async Task Load_AppliesInsightsToTiles()
    {
        using var f = Grouped();
        await f.LoadAsync();

        Assert.Equal(3, f.Tile(0).Insight.GroupSize);
        Assert.Equal("▣ 3", f.Tile(0).GroupBadgeText);
        Assert.True(f.Tile(1).ShowSharpestBadge);
        Assert.True(f.Tile(2).ShowBlurBadge);         // 50 < default 60
        Assert.Null(f.Tile(3).GroupBadgeText);
        Assert.Contains("likely blurry", f.Tile(2).AccessibleStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task G_SelectsTheFocusedPhotosGroup()
    {
        using var f = Grouped();
        await f.LoadAsync();
        IReadOnlyList<object>? requested = null;
        ((Controls.ISelectedItemsSink)f.Library).SelectionRequested += (_, items) => requested = items;
        f.Select(1);

        Assert.True(f.Library.TryHandle(CullingAction.SelectGroup));

        Assert.NotNull(requested);
        Assert.Equal(["IMG_00.jpg", "IMG_01.jpg", "IMG_02.jpg"], requested!.Cast<PhotoTileViewModel>().Select(t => t.FileName));
        Assert.Contains("group of 3", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task G_OnAnUngroupedPhoto_ExplainsWhyNothingWasSelected()
    {
        using var f = Grouped();
        await f.LoadAsync();
        var raised = false;
        ((Controls.ISelectedItemsSink)f.Library).SelectionRequested += (_, _) => raised = true;
        f.Select(3);

        f.Library.TryHandle(CullingAction.SelectGroup);

        Assert.False(raised);
        Assert.Contains("No similar photos", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task I_ShowsTheNumbersBehindTheBadges_ForTheCurrentPhoto()
    {
        using var f = Grouped();
        await f.LoadAsync();
        f.Select(2);

        f.Library.TryHandle(CullingAction.ToggleDetails);

        Assert.True(f.AnalysisPanel.IsDetailsOpen);
        Assert.Same(f.Tile(2), f.AnalysisPanel.DetailsTile);
        Assert.Contains(f.AnalysisPanel.Details, d => d.Topic == "Sharpness" && d.IsFlagged);
        Assert.Contains(f.AnalysisPanel.Details, d => d.Topic == "Similar photos");

        f.Library.OpenViewerCommand.Execute(f.Tile(0));
        Assert.Same(f.Tile(0), f.AnalysisPanel.DetailsTile); // follows the viewer's photo

        f.Library.TryHandle(CullingAction.ToggleHelp);
        Assert.False(f.AnalysisPanel.IsDetailsOpen);          // the side panels share the space
    }

    [Fact]
    public async Task SimilarGroupsFilter_ShowsOnlyGroupedPhotos_GroupByGroup()
    {
        using var f = Grouped();
        await f.LoadAsync();

        f.Library.Filter.Analysis = AnalysisFilterChoice.InGroup;

        Assert.Equal(["IMG_00.jpg", "IMG_01.jpg", "IMG_02.jpg", "IMG_04.jpg", "IMG_05.jpg"], f.Library.Photos.Select(t => t.FileName));
    }

    [Fact]
    public async Task BlurryFilter_UsesTheCurrentThreshold()
    {
        using var f = Grouped();
        await f.LoadAsync();

        f.Library.Filter.Analysis = AnalysisFilterChoice.LikelyBlurry;

        Assert.Equal(["IMG_02.jpg"], f.Library.Photos.Select(t => t.FileName));
    }

    [Fact]
    public async Task ChangingThresholds_RegroupsFromCache_WithoutReAnalysing()
    {
        using var f = Grouped();
        await f.LoadAsync();
        Assert.Equal(3, f.Tile(0).Insight.GroupSize);

        await f.Settings.SetAsync(AppSettings.AnalysisMaxHashDistance, 0, Ct);
        await f.Settings.SetAsync(AppSettings.AnalysisBlurThreshold, 150.0, Ct);
        await f.Library.OnNavigatedToAsync(Ct); // returning from the Settings page

        Assert.Null(f.Tile(0).Insight.GroupKey);    // distance 1 no longer counts as similar
        Assert.True(f.Tile(0).ShowBlurBadge);       // 120 < 150 now
        Assert.Equal(0, f.AnalysisService.AnalyzeCalls);
    }

    [Fact]
    public async Task Analyze_RunsTheService_AndReportsASummary()
    {
        using var f = Grouped();
        await f.LoadAsync();

        await f.AnalysisPanel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(1, f.AnalysisService.AnalyzeCalls);
        Assert.Contains("Analysis finished", f.Library.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("2 similar group(s)", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortcutMap_IncludesAnalysisKeys()
    {
        Assert.Equal(CullingAction.SelectGroup, CullingShortcuts.Resolve(System.Windows.Input.Key.G, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(CullingAction.ToggleDetails, CullingShortcuts.Resolve(System.Windows.Input.Key.I, System.Windows.Input.ModifierKeys.None));
        Assert.Contains(CullingShortcuts.Help.SelectMany(g => g.Items), i => i.Keys == "G");
    }
}
