using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    AutoApplied,
}

/// <summary>One entry in the Folder filter. <see cref="Path"/> is null for "All folders".</summary>
public sealed record FolderOption(string? Path, string Label, int Count)
{
    public string Display => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{Label}  ·  {Count:N0}");

    /// <summary>The full path, as a tooltip, so folders with the same name can be told apart.</summary>
    public string ToolTip => Path ?? "Every imported folder";
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
            AnalysisFilterChoice.AutoApplied => tile.AutoApplied,
            _ => true,
        };
    }

    partial void OnAnalysisChanged(AnalysisFilterChoice value) => RaiseChanged();

    /// <summary>"All folders" first, then every folder that holds photos (see <see cref="LibraryFolders"/>).</summary>
    public System.Collections.ObjectModel.ObservableCollection<FolderOption> Folders { get; } = [new FolderOption(null, "All folders", 0)];

    /// <summary>The folder the grid is limited to (with its subfolders); "All folders" = no limit.</summary>
    [ObservableProperty]
    private FolderOption? _folder;

    /// <summary>The Folder filter is worth showing only when there is more than one folder to choose from.</summary>
    public bool HasSeveralFolders => Folders.Count > 2;

    /// <summary>Refreshes the folder list after the library changes, keeping the chosen folder if it still has photos.</summary>
    public void SetFolders(IEnumerable<string> photoFolders)
    {
        ArgumentNullException.ThrowIfNull(photoFolders);
        var list = photoFolders.ToList();
        var chosen = Folder?.Path;
        _suspendChanged = true;
        try
        {
            Folders.Clear();
            Folders.Add(new FolderOption(null, "All folders", list.Count));
            foreach (var folder in LibraryFolders.Build(list))
            {
                Folders.Add(new FolderOption(folder.Path, folder.Label, folder.Count));
            }

            Folder = Folders.FirstOrDefault(f => chosen is not null && string.Equals(f.Path, chosen, StringComparison.OrdinalIgnoreCase)) ?? Folders[0];
        }
        finally
        {
            _suspendChanged = false;
        }

        OnPropertyChanged(nameof(HasSeveralFolders));
        NotifyActive();
        if (chosen is not null && Folder.Path is null)
        {
            Changed?.Invoke(this, EventArgs.Empty); // the chosen folder has gone: show everything
        }
    }

    partial void OnFolderChanged(FolderOption? value) => RaiseChanged();

    [ObservableProperty]
    private StateFilterChoice _state = StateFilterChoice.All;

    [ObservableProperty]
    private RatingFilterChoice _minRating = RatingFilterChoice.Any;

    [ObservableProperty]
    private LabelFilterChoice _label = LabelFilterChoice.Any;

    [ObservableProperty]
    private LibrarySort _sort = LibrarySort.FolderAndName;

    public event EventHandler? Changed;

    public bool IsFiltering => IsFolderActive || IsStateActive || IsRatingActive || IsLabelActive || IsAnalysisActive;

    public bool IsFolderActive => Folder?.Path is not null;

    /// <summary>Per-group "narrowing the view" flags, so the UI can mark which groups are active.</summary>
    public bool IsStateActive => State != StateFilterChoice.All;

    public bool IsRatingActive => MinRating != RatingFilterChoice.Any;

    public bool IsLabelActive => Label != LabelFilterChoice.Any;

    public bool IsAnalysisActive => Analysis != AnalysisFilterChoice.Any;

    private bool _suspendChanged;

    /// <summary>Resets every filter to All/Any in one step (one rebuild). Sorting is kept: it hides nothing.</summary>
    [RelayCommand(CanExecute = nameof(IsFiltering))]
    private void ClearFilters()
    {
        _suspendChanged = true;
        try
        {
            State = StateFilterChoice.All;
            MinRating = RatingFilterChoice.Any;
            Label = LabelFilterChoice.Any;
            Analysis = AnalysisFilterChoice.Any;
            Folder = Folders[0];
        }
        finally
        {
            _suspendChanged = false;
        }

        RaiseChanged();
    }

    public LibraryFilter ToFilter() => new()
    {
        Folder = Folder?.Path,
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
        if (_suspendChanged)
        {
            return;
        }

        NotifyActive();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyActive()
    {
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(IsFolderActive));
        OnPropertyChanged(nameof(IsStateActive));
        OnPropertyChanged(nameof(IsRatingActive));
        OnPropertyChanged(nameof(IsLabelActive));
        OnPropertyChanged(nameof(IsAnalysisActive));
        ClearFiltersCommand.NotifyCanExecuteChanged();
    }
}
