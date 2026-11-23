using FrameForge.App.ViewModels;
using FrameForge.Core.Library;

namespace FrameForge.App.Tests;

public sealed class LibraryFilterViewModelTests
{
    [Fact]
    public void ActiveFlags_FollowEachGroup()
    {
        var filter = new LibraryFilterViewModel();
        Assert.False(filter.IsFiltering);
        Assert.False(filter.ClearFiltersCommand.CanExecute(null));

        filter.Label = LabelFilterChoice.Red;
        filter.Analysis = AnalysisFilterChoice.AutoApplied;

        Assert.True(filter.IsLabelActive);
        Assert.True(filter.IsAnalysisActive);
        Assert.False(filter.IsStateActive);
        Assert.False(filter.IsRatingActive);
        Assert.True(filter.IsFiltering);
        Assert.True(filter.ClearFiltersCommand.CanExecute(null));
    }

    [Fact]
    public void ClearFilters_ResetsEveryFilter_InOneChange_AndKeepsTheSort()
    {
        var filter = new LibraryFilterViewModel
        {
            State = StateFilterChoice.Keep,
            MinRating = RatingFilterChoice.Three,
            Label = LabelFilterChoice.Blue,
            Analysis = AnalysisFilterChoice.Clipping,
            Sort = LibrarySort.Rating,
        };
        var changes = 0;
        filter.Changed += (_, _) => changes++;

        filter.ClearFiltersCommand.Execute(null);

        Assert.Equal(1, changes); // one grid rebuild, not four
        Assert.False(filter.IsFiltering);
        Assert.Equal(StateFilterChoice.All, filter.State);
        Assert.Equal(RatingFilterChoice.Any, filter.MinRating);
        Assert.Equal(LabelFilterChoice.Any, filter.Label);
        Assert.Equal(AnalysisFilterChoice.Any, filter.Analysis);
        Assert.Equal(LibrarySort.Rating, filter.Sort);
        Assert.False(filter.ClearFiltersCommand.CanExecute(null));
    }
}

public sealed class FolderFilterViewModelTests
{
    private static readonly string[] TwoFolders = [@"C:\Club\Match 1", @"C:\Club\Match 1", @"C:\Poen\Randoms"];

    [Fact]
    public void TwoFolders_AreOffered_AfterAllFolders_AndNothingIsFilteredYet()
    {
        var filter = new LibraryFilterViewModel();

        filter.SetFolders(TwoFolders);

        Assert.True(filter.HasSeveralFolders);
        Assert.Equal(["All folders  ·  3", "Match 1  ·  2", "Randoms  ·  1"], filter.Folders.Select(f => f.Display));
        Assert.Null(filter.Folder!.Path);
        Assert.False(filter.IsFiltering);
        Assert.Null(filter.ToFilter().Folder);
    }

    [Fact]
    public void OneFolder_HidesTheFolderFilter()
    {
        var filter = new LibraryFilterViewModel();

        filter.SetFolders([@"C:\Poen\Randoms"]);

        Assert.False(filter.HasSeveralFolders);
    }

    [Fact]
    public void ChoosingAFolder_FiltersTheView_AndClearFiltersShowsEverythingAgain()
    {
        var filter = new LibraryFilterViewModel();
        filter.SetFolders(TwoFolders);
        var changes = 0;
        filter.Changed += (_, _) => changes++;

        filter.Folder = filter.Folders[2];

        Assert.Equal(1, changes);
        Assert.True(filter.IsFolderActive);
        Assert.True(filter.IsFiltering);
        Assert.Equal(@"C:\Poen\Randoms", filter.ToFilter().Folder);

        filter.ClearFiltersCommand.Execute(null);
        Assert.Null(filter.ToFilter().Folder);
        Assert.False(filter.IsFolderActive);
    }

    [Fact]
    public void Reimporting_KeepsTheChosenFolder_OrFallsBackToAllWhenItHasGone()
    {
        var filter = new LibraryFilterViewModel();
        filter.SetFolders(TwoFolders);
        filter.Folder = filter.Folders[2];

        filter.SetFolders([.. TwoFolders, @"C:\New"]);
        Assert.Equal(@"C:\Poen\Randoms", filter.Folder!.Path);

        var changes = 0;
        filter.Changed += (_, _) => changes++;
        filter.SetFolders([@"C:\Club\Match 1", @"C:\New"]);
        Assert.Null(filter.Folder!.Path);
        Assert.Equal(1, changes); // the grid is rebuilt to show everything
    }
}
