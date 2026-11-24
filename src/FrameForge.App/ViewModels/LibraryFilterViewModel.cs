using System.Collections.ObjectModel;
using System.Globalization;
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

/// <summary>The sidebar's library views: a decision state, the last import, or files with problems.</summary>
public enum LibraryScope
{
    All,
    Undecided,
    Kept,
    ToReview,
    Rejected,
    LastImport,
    Problems,
}

/// <summary>One sidebar library view with its live count.</summary>
public sealed partial class LibraryScopeItem(LibraryScope scope, string title, string toolTip) : ObservableObject
{
    public LibraryScope Scope { get; } = scope;

    public string Title { get; } = title;

    public string ToolTip { get; } = toolTip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _count;

    /// <summary>False hides the view (Problems with no problems, Last import before any import).</summary>
    [ObservableProperty]
    private bool _isAvailable = true;

    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>One entry in the Folder filter. <see cref="Path"/> is null for "All folders".</summary>
public sealed record FolderOption(string? Path, string Label, int Count, int Depth = 0, string? Name = null)
{
    public string Display => string.Create(CultureInfo.CurrentCulture, $"{Label}  ·  {Count:N0}");

    /// <summary>Name in the sidebar tree: the folder below its parent, or the label for a top-level folder.</summary>
    public string TreeName => Name ?? Label;

    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>The full path, as a tooltip, so folders with the same name can be told apart.</summary>
    public string ToolTip => Path ?? "Every imported folder";
}

/// <summary>Filter and sort choices for the grid. Raises <see cref="Changed"/> when any choice changes.</summary>
public sealed partial class LibraryFilterViewModel : ObservableObject
{
    private bool _suspendChanged;

    public LibraryFilterViewModel()
    {
        Scopes =
        [
            new(LibraryScope.All, "All photos", "Every photo (decision filters off)"),
            new(LibraryScope.Undecided, "Undecided", "No Keep, Review or Reject yet"),
            new(LibraryScope.Kept, "Kept", "Marked Keep (P)"),
            new(LibraryScope.ToReview, "To review", "Marked Review (V)"),
            new(LibraryScope.Rejected, "Rejected", "Marked Reject (X)"),
            new(LibraryScope.LastImport, "Last import", "Photos first added by the most recent import that added any") { IsAvailable = false },
            new(LibraryScope.Problems, "Problems", "Missing or unreadable files") { IsAvailable = false },
        ];
        _selectedScope = Scopes[0];
    }

    /// <summary>The sidebar's library views, in display order.</summary>
    public IReadOnlyList<LibraryScopeItem> Scopes { get; }

    /// <summary>The chosen sidebar view. Sets <see cref="State"/> and <see cref="RecentOnly"/> in one change.</summary>
    [ObservableProperty]
    private LibraryScopeItem _selectedScope;

    partial void OnSelectedScopeChanged(LibraryScopeItem value)
    {
        if (_syncingScope || value is null)
        {
            return;
        }

        var wasSuspended = _suspendChanged;
        _suspendChanged = true;
        _syncingScope = true; // the chosen scope is already right; don't re-derive it halfway through
        try
        {
            RecentOnly = value.Scope == LibraryScope.LastImport;
            State = value.Scope switch
            {
                LibraryScope.Undecided => StateFilterChoice.Unset,
                LibraryScope.Kept => StateFilterChoice.Keep,
                LibraryScope.ToReview => StateFilterChoice.Review,
                LibraryScope.Rejected => StateFilterChoice.Reject,
                LibraryScope.Problems => StateFilterChoice.Problems,
                _ => StateFilterChoice.All,
            };
        }
        finally
        {
            _suspendChanged = wasSuspended;
            _syncingScope = false;
        }

        RaiseChanged();
    }

    private bool _syncingScope;

    /// <summary>Keeps the sidebar selection in step when <see cref="State"/> or <see cref="RecentOnly"/> is set directly.</summary>
    private void SyncScope()
    {
        if (_syncingScope)
        {
            return;
        }

        var scope = RecentOnly ? LibraryScope.LastImport : State switch
        {
            StateFilterChoice.Unset => LibraryScope.Undecided,
            StateFilterChoice.Keep => LibraryScope.Kept,
            StateFilterChoice.Review => LibraryScope.ToReview,
            StateFilterChoice.Reject => LibraryScope.Rejected,
            StateFilterChoice.Problems => LibraryScope.Problems,
            _ => LibraryScope.All,
        };
        _syncingScope = true;
        try
        {
            SelectedScope = Scopes.First(s => s.Scope == scope);
        }
        finally
        {
            _syncingScope = false;
        }
    }

    /// <summary>When photos were last added by an import (UTC); null before the first import.</summary>
    public DateTime? LastImportUtc { get; private set; }

    /// <summary>Only photos added by the most recent import that added any (see <see cref="LastImportUtc"/>).</summary>
    [ObservableProperty]
    private bool _recentOnly;

    partial void OnRecentOnlyChanged(bool value)
    {
        SyncScope();
        RaiseChanged();
    }

    /// <summary>Updates the sidebar counts and the last-import time after the library or a decision changes.</summary>
    public void SetCounts(IReadOnlyDictionary<LibraryScope, int> counts, DateTime? lastImportUtc)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var importChanged = LastImportUtc != lastImportUtc;
        LastImportUtc = lastImportUtc;
        foreach (var scope in Scopes)
        {
            scope.Count = counts.GetValueOrDefault(scope.Scope);
        }

        Scopes.First(s => s.Scope == LibraryScope.LastImport).IsAvailable = lastImportUtc is not null;
        Scopes.First(s => s.Scope == LibraryScope.Problems).IsAvailable = counts.GetValueOrDefault(LibraryScope.Problems) > 0
            || SelectedScope.Scope == LibraryScope.Problems;
        if (importChanged && RecentOnly)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

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
    public ObservableCollection<FolderOption> Folders { get; } = [new FolderOption(null, "All folders", 0)];

    /// <summary>The folder the grid is limited to (with its subfolders); "All folders" = no limit.</summary>
    [ObservableProperty]
    private FolderOption? _folder;

    /// <summary>The Folder filter is worth showing only when there is more than one folder to choose from.</summary>
    public bool HasSeveralFolders => Folders.Count > 2;

    /// <summary>The sidebar lists folders as soon as there is one.</summary>
    public bool HasFolders => Folders.Count > 1;

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
                Folders.Add(new FolderOption(folder.Path, folder.Label, folder.Count, folder.Depth, folder.Name));
            }

            Folder = Folders.FirstOrDefault(f => chosen is not null && string.Equals(f.Path, chosen, StringComparison.OrdinalIgnoreCase)) ?? Folders[0];
        }
        finally
        {
            _suspendChanged = false;
        }

        OnPropertyChanged(nameof(HasSeveralFolders));
        OnPropertyChanged(nameof(HasFolders));
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

    public bool IsFiltering => IsFolderActive || IsStateActive || IsRatingActive || IsLabelActive || IsAnalysisActive || RecentOnly;

    public bool IsFolderActive => Folder?.Path is not null;

    /// <summary>Per-group "narrowing the view" flags, so the UI can mark which groups are active.</summary>
    public bool IsStateActive => State != StateFilterChoice.All;

    public bool IsRatingActive => MinRating != RatingFilterChoice.Any;

    public bool IsLabelActive => Label != LabelFilterChoice.Any;

    public bool IsAnalysisActive => Analysis != AnalysisFilterChoice.Any;

    /// <summary>True while a toolbar filter (rating, label, analysis) narrows the view; the sidebar shows its own.</summary>
    public bool IsRefining => IsRatingActive || IsLabelActive || IsAnalysisActive;

    /// <summary>How many toolbar filters are on, for the "Clear" button.</summary>
    public int RefineCount => (IsRatingActive ? 1 : 0) + (IsLabelActive ? 1 : 0) + (IsAnalysisActive ? 1 : 0);

    /// <summary>Resets every filter to All/Any in one step (one rebuild). Sorting is kept: it hides nothing.</summary>
    [RelayCommand(CanExecute = nameof(IsFiltering))]
    private void ClearFilters()
    {
        _suspendChanged = true;
        try
        {
            State = StateFilterChoice.All;
            RecentOnly = false;
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

    /// <summary>Resets only the toolbar filters (rating, label, analysis); the sidebar view and folder stay.</summary>
    [RelayCommand(CanExecute = nameof(IsRefining))]
    private void ClearRefinements()
    {
        _suspendChanged = true;
        try
        {
            MinRating = RatingFilterChoice.Any;
            Label = LabelFilterChoice.Any;
            Analysis = AnalysisFilterChoice.Any;
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
        ImportedSinceUtc = RecentOnly ? LastImportUtc ?? DateTime.MaxValue : null,
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

    partial void OnStateChanged(StateFilterChoice value)
    {
        // A state chosen directly (tests, code) ends "Last import" and moves the sidebar selection with it.
        if (!_syncingScope && value != StateFilterChoice.All && RecentOnly)
        {
            var wasSuspended = _suspendChanged;
            _suspendChanged = true;
            RecentOnly = false;
            _suspendChanged = wasSuspended;
        }

        SyncScope();
        RaiseChanged();
    }

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
        OnPropertyChanged(nameof(IsRefining));
        OnPropertyChanged(nameof(RefineCount));
        ClearFiltersCommand.NotifyCanExecuteChanged();
        ClearRefinementsCommand.NotifyCanExecuteChanged();
    }
}
