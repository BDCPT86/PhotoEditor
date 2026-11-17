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
