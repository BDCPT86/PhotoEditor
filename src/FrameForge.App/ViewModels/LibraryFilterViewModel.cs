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

/// <summary>Filter and sort choices for the grid. Raises <see cref="Changed"/> when any choice changes.</summary>
public sealed partial class LibraryFilterViewModel : ObservableObject
{
    [ObservableProperty]
    private StateFilterChoice _state = StateFilterChoice.All;

    [ObservableProperty]
    private RatingFilterChoice _minRating = RatingFilterChoice.Any;

    [ObservableProperty]
    private LabelFilterChoice _label = LabelFilterChoice.Any;

    [ObservableProperty]
    private LibrarySort _sort = LibrarySort.FolderAndName;

    public event EventHandler? Changed;

    public bool IsFiltering => State != StateFilterChoice.All || MinRating != RatingFilterChoice.Any || Label != LabelFilterChoice.Any;

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
