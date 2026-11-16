using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.Core.Decisions;
using FrameForge.Core.Library;

namespace FrameForge.App.ViewModels;

public enum StateFilterChoice
{
    All,
    Unset,
    Keep,
    Review,
    Reject,
    Problems,
}

public enum RatingFilterChoice
{
    Any = 0,
    One = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
}

public enum LabelFilterChoice
{
    Any,
    Red,
    Yellow,
    Green,
    Blue,
}

public enum AnalysisFilterChoice
{
    Any,
    LikelyBlurry,
    Clipping,
    InGroup,
    ExactDuplicate,
    AiPick,
    AiFacesSoft,
}

/// <summary>Filter and sort choices for the grid. Raises <see cref="Changed"/> when any choice changes.</summary>
public sealed partial class LibraryFilterViewModel : ObservableObject
{
    /// <summary>Heuristic analysis filter. "In a group" also orders photos group by group.</summary>
    [ObservableProperty]
    private AnalysisFilterChoice _analysis = AnalysisFilterChoice.Any;

    /// <summary>True when the tile passes the analysis filter.</summary>
    public bool MatchesAnalysis(PhotoTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var insight = tile.Insight;
        return Analysis switch
        {
            AnalysisFilterChoice.LikelyBlurry => insight.LikelyBlurry,
            AnalysisFilterChoice.Clipping => insight.HighlightsClipped || insight.ShadowsClipped,
            AnalysisFilterChoice.InGroup => insight.GroupKey is not null,
            AnalysisFilterChoice.ExactDuplicate => insight.ExactDuplicateCount > 0,
            AnalysisFilterChoice.AiPick => tile.Ai.IsSuggestedPick,
            AnalysisFilterChoice.AiFacesSoft => tile.Ai.FacesSoft,
            _ => true,
        };
    }

    partial void OnAnalysisChanged(AnalysisFilterChoice value) => RaiseChanged();

    [ObservableProperty]
    private StateFilterChoice _state = StateFilterChoice.All;

    [ObservableProperty]
    private RatingFilterChoice _minRating = RatingFilterChoice.Any;

    [ObservableProperty]
    private LabelFilterChoice _label = LabelFilterChoice.Any;

    [ObservableProperty]
    private LibrarySort _sort = LibrarySort.FolderAndName;

    public event EventHandler? Changed;

    public bool IsFiltering => State != StateFilterChoice.All || MinRating != RatingFilterChoice.Any || Label != LabelFilterChoice.Any || Analysis != AnalysisFilterChoice.Any;

    public LibraryFilter ToFilter() => new()
    {
        ProblemsOnly = State == StateFilterChoice.Problems,
        States = State switch
        {
            StateFilterChoice.Unset => new HashSet<CullState> { CullState.Unset },
            StateFilterChoice.Keep => new HashSet<CullState> { CullState.Keep },
            StateFilterChoice.Review => new HashSet<CullState> { CullState.Review },
            StateFilterChoice.Reject => new HashSet<CullState> { CullState.Reject },
            _ => null,
        },
        MinRating = (int)MinRating,
        Labels = Label switch
        {
            LabelFilterChoice.Red => new HashSet<ColourLabel> { ColourLabel.Red },
            LabelFilterChoice.Yellow => new HashSet<ColourLabel> { ColourLabel.Yellow },
            LabelFilterChoice.Green => new HashSet<ColourLabel> { ColourLabel.Green },
            LabelFilterChoice.Blue => new HashSet<ColourLabel> { ColourLabel.Blue },
            _ => null,
        },
    };

    partial void OnStateChanged(StateFilterChoice value) => RaiseChanged();

    partial void OnMinRatingChanged(RatingFilterChoice value) => RaiseChanged();

    partial void OnLabelChanged(LabelFilterChoice value) => RaiseChanged();

    partial void OnSortChanged(LibrarySort value) => RaiseChanged();

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(IsFiltering));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
