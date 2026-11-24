using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Settings;

namespace FrameForge.App.Tests;

/// <summary>The redesigned library: sidebar views with counts, the last import, the heading, and grid density.</summary>
public sealed class LibrarySidebarTests
{
    private static LibraryScopeItem Scope(LibraryFilterViewModel filter, LibraryScope scope) => filter.Scopes.Single(s => s.Scope == scope);

    [Fact]
    public void ChoosingAView_SetsTheDecisionFilter_InOneChange()
    {
        var filter = new LibraryFilterViewModel();
        var changes = 0;
        filter.Changed += (_, _) => changes++;

        filter.SelectedScope = Scope(filter, LibraryScope.Kept);

        Assert.Equal(1, changes);
        Assert.Equal(StateFilterChoice.Keep, filter.State);
        Assert.False(filter.RecentOnly);
    }

    [Fact]
    public void SettingTheStateDirectly_MovesTheSidebarSelection()
    {
        var filter = new LibraryFilterViewModel();

        filter.State = StateFilterChoice.Reject;

        Assert.Equal(LibraryScope.Rejected, filter.SelectedScope.Scope);
    }

    [Fact]
    public void LastImport_WithoutAnImport_ShowsNothing_AndIsHidden()
    {
        var filter = new LibraryFilterViewModel();
        Assert.False(Scope(filter, LibraryScope.LastImport).IsAvailable);

        filter.SelectedScope = Scope(filter, LibraryScope.LastImport);

        Assert.True(filter.RecentOnly);
        Assert.Equal(DateTime.MaxValue, filter.ToFilter().ImportedSinceUtc);
    }

    [Fact]
    public void ClearRefinements_KeepsTheViewAndFolder()
    {
        var filter = new LibraryFilterViewModel();
        filter.SetFolders([@"C:\A", @"C:\B"]);
        filter.SelectedScope = Scope(filter, LibraryScope.ToReview);
        filter.Folder = filter.Folders[1];
        filter.MinRating = RatingFilterChoice.Three;
        filter.Analysis = AnalysisFilterChoice.Clipping;
        Assert.Equal(2, filter.RefineCount);

        filter.ClearRefinementsCommand.Execute(null);

        Assert.False(filter.IsRefining);
        Assert.Equal(StateFilterChoice.Review, filter.State);
        Assert.Equal(@"C:\A", filter.Folder!.Path);
    }

    [Fact]
    public async Task SidebarCounts_CoverTheWholeLibrary_AndFollowDecisions()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0, 1);

        f.Library.TryHandle(CullingAction.Keep);

        var filter = f.Library.Filter;
        Assert.Equal(6, Scope(filter, LibraryScope.All).Count);
        Assert.Equal(2, Scope(filter, LibraryScope.Kept).Count);
        Assert.Equal(4, Scope(filter, LibraryScope.Undecided).Count);
        Assert.False(Scope(filter, LibraryScope.Problems).IsAvailable); // no problems: the view is not offered
        Assert.Contains("2 keep", f.Library.DecisionSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LastImport_ShowsOnlyThePhotosTheNewestImportAdded()
    {
        using var f = new CullingFixture();
        var earlier = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < f.Photos.Count; i++)
        {
            f.Photos[i].ImportedUtc = i < 4 ? earlier : latest;
        }

        await f.LoadAsync();
        var filter = f.Library.Filter;
        Assert.True(Scope(filter, LibraryScope.LastImport).IsAvailable);
        Assert.Equal(2, Scope(filter, LibraryScope.LastImport).Count);

        filter.SelectedScope = Scope(filter, LibraryScope.LastImport);

        Assert.Equal(["IMG_04.jpg", "IMG_05.jpg"], f.Library.Photos.Select(t => t.FileName));
        Assert.Equal("Last import", f.Library.ViewTitle);
    }

    [Fact]
    public async Task Heading_NamesTheViewAndFolder_AndCountsWhatTheRefinementsLeave()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.Rate4);

        f.Library.Filter.MinRating = RatingFilterChoice.Four;

        Assert.Equal("All photos", f.Library.ViewTitle);
        Assert.Equal("1 of 6", f.Library.ShownCountText);
    }

    [Fact]
    public async Task Density_StepsWithinThreeSizes_AndIsRemembered()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        Assert.Equal(GridDensity.Medium, f.Library.Density);
        var medium = f.Library.TileSize;

        f.Library.ChangeDensityCommand.Execute(1);
        f.Library.ChangeDensityCommand.Execute(1); // already the largest: stays
        Assert.Equal(GridDensity.Large, f.Library.Density);
        Assert.True(f.Library.TileSize.Width > medium.Width);

        f.Library.ChangeDensityCommand.Execute(-2);
        Assert.Equal(GridDensity.Compact, f.Library.Density);
        Assert.False(f.Library.ShowFileNames);

        for (var i = 0; i < 50 && await f.Settings.GetAsync(AppSettings.LibraryGridDensity, TestContext.Current.CancellationToken) != GridDensity.Compact; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(GridDensity.Compact, await f.Settings.GetAsync(AppSettings.LibraryGridDensity, TestContext.Current.CancellationToken));
    }
}
