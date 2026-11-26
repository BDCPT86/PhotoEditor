using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Controls;
using FrameForge.App.Services;
using FrameForge.App.Shortcuts;
using FrameForge.Application.Cleanup;
using FrameForge.Application.Decisions;
using FrameForge.Application.Export;
using FrameForge.Application.Photos;
using FrameForge.Application.Settings;
using FrameForge.Core.Decisions;
using FrameForge.Core.Library;
using FrameForge.Core.Photos;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Coordinates the Library page: grid (with filtering and multi-selection), import, viewer, compare,
/// export, and culling shortcuts. Decision logic lives in <see cref="DecisionController"/>; filtering in
/// <see cref="LibraryFilterViewModel"/> and <see cref="LibraryQuery"/>.
/// </summary>
/// <remarks>
/// Selection (highlighted tiles), focus (the current photo in viewer/compare), and decisions (Keep/Reject…)
/// are kept separate: decision keys apply to the selection in the grid, and to the current photo elsewhere.
/// </remarks>
public sealed partial class LibraryViewModel : PageViewModel, ICullingShortcutHandler, ISelectedItemsSink
{
    private readonly IPhotoLibraryService _library;
    private readonly IPhotoImportService _importer;
    private readonly IDecisionService _decisionService;
    private readonly ISettingsService _settings;
    private readonly IFolderPicker _folderPicker;
    private readonly IThumbnailSource _thumbnails;
    private readonly ILogger<LibraryViewModel> _logger;
    private List<PhotoTileViewModel> _all = [];
    private List<PhotoTileViewModel> _selection = [];
    private bool _autoAdvance = true;
    private bool _pairRawJpeg = true;
    private bool _loaded;

    public LibraryViewModel(
        IPhotoLibraryService library,
        IPhotoImportService importer,
        IDecisionService decisionService,
        ISettingsService settings,
        IFolderPicker folderPicker,
        IThumbnailSource thumbnails,
        DecisionController decisions,
        LibraryFilterViewModel filter,
        ViewerViewModel viewer,
        CompareViewModel compare,
        ExportViewModel export,
        LibraryAnalysisViewModel analysis,
        PresetPanelViewModel presets,
        LocateFolderViewModel locate,
        RecycleRejectsViewModel recycle,
        ILogger<LibraryViewModel> logger)
    {
        Analysis = analysis;
        Presets = presets;
        Locate = locate;
        Locate.Relocated += (_, moved) => _ = OnRelocatedAsync(moved);
        Recycle = recycle;
        Recycle.Completed += (_, completed) => OnRecycleCompleted(completed);
        _library = library;
        _importer = importer;
        _decisionService = decisionService;
        _settings = settings;
        _folderPicker = folderPicker;
        _thumbnails = thumbnails;
        _logger = logger;
        Decisions = decisions;
        Filter = filter;
        Viewer = viewer;
        Compare = compare;
        Export = export;

        Viewer.CloseRequested += (_, _) => _ = CloseViewerAsync();
        Compare.CloseRequested += (_, _) => CloseCompare();
        Filter.Changed += (_, _) => RebuildView();
        Decisions.DecisionsChanged += (_, change) => OnDecisionsChanged(change);

        Analysis.RunFinished += (_, result) => SetStatus(result.Message, result.IsWarning);
        Analysis.Controller.InsightsChanged += (_, _) => OnInsightsChanged();
        Edits.AutoMarksChanged += (_, _) =>
        {
            if (Filter.Analysis == AnalysisFilterChoice.AutoApplied && !IsViewerOpen && !IsCompareOpen)
            {
                RebuildView();
            }
        };
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewerViewModel.Current))
            {
                UpdateDetailsTarget();
            }
        };
        Compare.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CompareViewModel.Active))
            {
                UpdateDetailsTarget();
            }
        };
    }

    public LibraryAnalysisViewModel Analysis { get; }

    /// <summary>Offline folders and Locate folder… for a folder that moved.</summary>
    public LocateFolderViewModel Locate { get; }

    /// <summary>The "Move rejects to Recycle Bin" panel (M09).</summary>
    public RecycleRejectsViewModel Recycle { get; }

    /// <summary>The Rejected view is shown and has photos: the Recycle Bin command applies.</summary>
    public bool CanRecycleRejects => Filter.SelectedScope.Scope == LibraryScope.Rejected && Photos.Count > 0;

    /// <summary>Preset batch panel; its controller also owns every tile's edit recipe.</summary>
    public PresetPanelViewModel Presets { get; }

    public EditController Edits => Presets.Edits;

    private bool IsDeveloping => IsViewerOpen && Viewer.Develop.IsOpen;

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<object>>? SelectionRequested;

    public override string Title => "Library";

    public DecisionController Decisions { get; }

    public LibraryFilterViewModel Filter { get; }

    public ViewerViewModel Viewer { get; }

    public CompareViewModel Compare { get; }

    public ExportViewModel Export { get; }

    public IReadOnlyList<ShortcutHelpGroup> ShortcutHelp { get; } = CullingShortcuts.Help;

    /// <summary>The photos shown in the grid: the library after filter and sort. Always the same collection (see <see cref="TileCollection"/>).</summary>
    public TileCollection Photos { get; } = [];

    /// <summary>The focused/primary selected tile in the grid.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenViewerCommand))]
    private PhotoTileViewModel? _selectedPhoto;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    private bool _isViewerOpen;

    [ObservableProperty]
    private bool _isCompareOpen;

    /// <summary>Shortcut help side panel. Opening it closes the analysis details panel (they share the space).</summary>
    [ObservableProperty]
    private bool _isHelpOpen;

    partial void OnIsHelpOpenChanged(bool value)
    {
        if (value)
        {
            Analysis.IsDetailsOpen = false;
        }
    }

    partial void OnSelectedPhotoChanged(PhotoTileViewModel? value) => UpdateDetailsTarget();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isImporting;

    [ObservableProperty]
    private string? _importProgressText;

    [ObservableProperty]
    private double _importProgressValue;

    [ObservableProperty]
    private bool _isImportProgressIndeterminate;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsWarning;

    public bool IsEmpty => _all.Count == 0 && !IsLoading && !IsImporting;

    public bool IsFilteredEmpty => _all.Count > 0 && Photos.Count == 0;

    public string CountText
    {
        get
        {
            if (_all.Count == 0)
            {
                return string.Empty;
            }

            var culture = CultureInfo.CurrentCulture;
            var counts = CountScopes();
            var shown = Photos.Count == _all.Count
                ? string.Create(culture, $"{_all.Count:N0} photos")
                : string.Create(culture, $"{Photos.Count:N0} of {_all.Count:N0} shown");
            var text = string.Create(culture, $"{shown}  ·  {counts[LibraryScope.Kept]:N0} keep  ·  {counts[LibraryScope.ToReview]:N0} review  ·  {counts[LibraryScope.Rejected]:N0} reject");
            var problems = counts[LibraryScope.Problems];
            return problems > 0 ? text + string.Create(culture, $"  ·  {problems:N0} problem(s)") : text;
        }
    }

    /// <summary>Decision totals for the footer (whole library), e.g. "312 keep · 40 review · 280 reject".</summary>
    public string DecisionSummaryText
    {
        get
        {
            if (_all.Count == 0)
            {
                return string.Empty;
            }

            var counts = CountScopes();
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{counts[LibraryScope.Kept]:N0} keep  ·  {counts[LibraryScope.ToReview]:N0} review  ·  {counts[LibraryScope.Rejected]:N0} reject  ·  {counts[LibraryScope.Undecided]:N0} undecided");
        }
    }

    /// <summary>The page heading: the sidebar view, narrowed to a folder when one is chosen ("Kept · Randoms").</summary>
    public string ViewTitle => Filter.Folder?.Path is null
        ? Filter.SelectedScope.Title
        : $"{Filter.SelectedScope.Title}  ·  {Filter.Folder.TreeName}";

    /// <summary>Next to the heading: how many photos are shown ("84" or "84 of 1,010" when toolbar filters narrow it).</summary>
    public string ShownCountText
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            if (_all.Count == 0)
            {
                return string.Empty;
            }

            var scoped = LibraryQuery.Apply(_all, t => t.Photo, t => t.Decision, Filter.ToFilter() with { MinRating = 0, Labels = null }, Filter.Sort).Count;
            return Photos.Count == scoped
                ? Photos.Count.ToString("N0", culture)
                : string.Create(culture, $"{Photos.Count:N0} of {scoped:N0}");
        }
    }

    private Dictionary<LibraryScope, int> CountScopes()
    {
        var counts = Enum.GetValues<LibraryScope>().ToDictionary(s => s, _ => 0);
        var lastImport = LastImportUtc();
        foreach (var tile in _all)
        {
            var scope = tile.Decision.State switch
            {
                CullState.Keep => LibraryScope.Kept,
                CullState.Review => LibraryScope.ToReview,
                CullState.Reject => LibraryScope.Rejected,
                _ => LibraryScope.Undecided,
            };
            counts[scope]++;
            if (tile.Status != ImportStatus.Imported || tile.OfflineReason is not null)
            {
                counts[LibraryScope.Problems]++;
            }

            if (lastImport is { } since && tile.Photo.ImportedUtc >= since)
            {
                counts[LibraryScope.LastImport]++;
            }
        }

        counts[LibraryScope.All] = _all.Count;
        return counts;
    }

    /// <summary>
    /// One import stamps every photo it adds with the same time, and later imports keep that first time, so the
    /// newest stamp marks the photos the most recent import added.
    /// </summary>
    private DateTime? LastImportUtc() => _all.Count == 0 ? null : _all.Max(t => t.Photo.ImportedUtc);

    /// <summary>Refreshes the sidebar counts and the footer after the library or a decision changes.</summary>
    private void UpdateCounts()
    {
        Filter.SetCounts(CountScopes(), LastImportUtc());
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(DecisionSummaryText));
        OnPropertyChanged(nameof(ShownCountText));
    }

    // ---------------------------------------------------------------- grid density

    /// <summary>Thumbnail size in the grid (Ctrl+ − / Ctrl+ =). Remembered between sessions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TileSize), nameof(ShowFileNames))]
    private GridDensity _density = GridDensity.Medium;

    /// <summary>Cell size for the grid's wrap panel: image area plus the one-line caption.</summary>
    public System.Windows.Size TileSize => Density switch
    {
        GridDensity.Compact => new System.Windows.Size(136, 128),
        GridDensity.Large => new System.Windows.Size(272, 248),
        _ => new System.Windows.Size(196, 182),
    };

    /// <summary>Compact tiles drop the file name; decisions and ratings stay.</summary>
    public bool ShowFileNames => Density != GridDensity.Compact;

    partial void OnDensityChanged(GridDensity value)
    {
        if (_loaded)
        {
            _ = SaveSettingAsync(AppSettings.LibraryGridDensity, value);
        }
    }

    [RelayCommand]
    private void ChangeDensity(int step) =>
        Density = (GridDensity)Math.Clamp((int)Density + step, (int)GridDensity.Compact, (int)GridDensity.Large);

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            // Settings may have changed while on another page: thresholds re-evaluate instantly from cached results.
            _autoAdvance = await _settings.GetAsync(AppSettings.ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);
            await Analysis.Controller.RefreshSettingsAsync(cancellationToken).ConfigureAwait(true);

            // Showing pairs as one photo or as two files changes which tiles exist: rebuild them.
            var pair = await _settings.GetAsync(AppSettings.LibraryPairRawJpeg, cancellationToken).ConfigureAwait(true);
            if (pair != _pairRawJpeg)
            {
                _pairRawJpeg = pair;
                if (IsViewerOpen)
                {
                    await CloseViewerAsync().ConfigureAwait(true);
                }

                CloseCompare();
                await ReloadAsync(cancellationToken).ConfigureAwait(true);
            }

            return;
        }

        _loaded = true;
        try
        {
            IncludeSubfolders = await _settings.GetAsync(AppSettings.ImportIncludeSubfolders, cancellationToken).ConfigureAwait(true);
            Density = await _settings.GetAsync(AppSettings.LibraryGridDensity, cancellationToken).ConfigureAwait(true);
            _pairRawJpeg = await _settings.GetAsync(AppSettings.LibraryPairRawJpeg, cancellationToken).ConfigureAwait(true);
            _autoAdvance = await _settings.GetAsync(AppSettings.ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);
            await ReloadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loaded = false;
            LogLoadFailed(ex);
            SetStatus("The library could not be loaded. See the log for details.", warning: true);
        }
    }

    void ISelectedItemsSink.UpdateSelection(IReadOnlyList<object> items)
    {
        _selection = items.OfType<PhotoTileViewModel>().ToList();
        if (Presets.IsOpen)
        {
            Presets.RefreshTargets();
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (_loaded)
        {
            _ = SaveSettingAsync(AppSettings.ImportIncludeSubfolders, value);
        }
    }

    // ---------------------------------------------------------------- shortcuts and decisions

    public bool TryHandle(CullingAction action)
    {
        switch (action)
        {
            case CullingAction.Undo when IsDeveloping:
                Viewer.Develop.UndoCommand.Execute(null);
                return true;
            case CullingAction.Redo when IsDeveloping:
                Viewer.Develop.RedoCommand.Execute(null);
                return true;
            case CullingAction.Undo:
                _ = ReportAsync(Decisions.UndoAsync(), "Undid");
                return true;
            case CullingAction.Redo:
                _ = ReportAsync(Decisions.RedoAsync(), "Redid");
                return true;
            case CullingAction.ToggleDevelop:
                if (IsCompareOpen)
                {
                    return false;
                }

                if (!IsViewerOpen)
                {
                    OpenViewer(null);
                }

                if (IsViewerOpen)
                {
                    Viewer.ToggleDevelopCommand.Execute(null);
                }

                return true;
            case CullingAction.ToggleCrop when IsDeveloping:
                Viewer.Develop.ToggleCropModeCommand.Execute(null);
                return true;
            case CullingAction.BeforeAfter when IsDeveloping:
                Viewer.Develop.ToggleBeforeAfterCommand.Execute(null);
                return true;
            case CullingAction.ToggleMaskOverlay when IsDeveloping:
                Viewer.Develop.Masks.ToggleOverlayCommand.Execute(null);
                return true;
            case CullingAction.ToggleRefine when IsDeveloping:
                Viewer.Develop.Masks.ToggleRefineCommand.Execute(null);
                return true;
            case CullingAction.ToggleCrop or CullingAction.BeforeAfter or CullingAction.ToggleMaskOverlay or CullingAction.ToggleRefine:
                return false;
            case CullingAction.NextFace or CullingAction.PreviousFace when IsViewerOpen && !Viewer.Develop.IsOpen:
                Viewer.StepFace(action == CullingAction.NextFace ? 1 : -1);
                return true;
            case CullingAction.NextFace or CullingAction.PreviousFace:
                return false;
            case CullingAction.Compare:
                OpenCompare();
                return true;
            case CullingAction.RecycleRejects:
                OpenRecycleRejects();
                return true;
            case CullingAction.Refresh:
                RebuildView();
                _ = RecheckFoldersAsync(); // a drive plugged back in shows up without a restart
                return true;
            case CullingAction.ToggleHelp:
                IsHelpOpen = !IsHelpOpen;
                return true;
            case CullingAction.ToggleDetails:
                Analysis.IsDetailsOpen = !Analysis.IsDetailsOpen;
                if (Analysis.IsDetailsOpen)
                {
                    IsHelpOpen = false;
                    UpdateDetailsTarget();
                }

                return true;
            case CullingAction.SelectGroup:
                SelectGroup();
                return true;
        }

        return CullingShortcuts.ToEdit(action) is { } edit && ApplyEdit(edit, CullingShortcuts.IsStateAction(action));
    }

    /// <summary>Same as a key press: lets buttons (help, compare) share the shortcut path.</summary>
    [RelayCommand]
    private void RunShortcut(CullingAction action) => TryHandle(action);

    /// <summary>The photos a decision key applies to in the current mode.</summary>
    internal IReadOnlyList<PhotoTileViewModel> DecisionTargets()
    {
        if (IsCompareOpen)
        {
            return Compare.Active is { } panel ? [panel.Tile] : [];
        }

        if (IsViewerOpen)
        {
            return Viewer.Current is { } current ? [current] : [];
        }

        if (_selection.Count > 0)
        {
            return _selection;
        }

        return SelectedPhoto is { } selected ? [selected] : [];
    }

    private bool ApplyEdit(DecisionEdit edit, bool isStateEdit)
    {
        var targets = DecisionTargets();
        if (targets.Count == 0)
        {
            return false;
        }

        // ApplyAsync updates the tiles synchronously before its first await, so advancing
        // afterwards shows the next photo while the save completes in the background.
        var pending = Decisions.ApplyAsync(targets, edit);
        if (IsViewerOpen && isStateEdit && _autoAdvance)
        {
            Viewer.NextCommand.Execute(null);
        }

        _ = ReportAsync(pending, null);
        return true;
    }

    private async Task ReportAsync(Task<DecisionOutcome> pending, string? verb)
    {
        var outcome = await pending.ConfigureAwait(true);
        if (outcome.Error is { } error)
        {
            SetStatus(error, warning: true);
        }
        else if (outcome.Change is { } change)
        {
            var count = Decisions.PhotoCount(change);
            var what = verb is null ? change.Description : $"{verb}: {change.Description.ToLower(CultureInfo.CurrentCulture)}";
            SetStatus(count == 1 ? what : string.Create(CultureInfo.CurrentCulture, $"{what} · {count:N0} photos"), warning: false);
        }
    }

    /// <summary>Selects every photo in the focused photo's near-duplicate group (grid only).</summary>
    private void SelectGroup()
    {
        if (IsViewerOpen || IsCompareOpen)
        {
            return;
        }

        if (SelectedPhoto is not { } focused)
        {
            SetStatus("Select a photo first, then press G to select its similar group.", warning: false);
            return;
        }

        if (!focused.Insight.IsAnalysed)
        {
            SetStatus("This photo has not been analysed yet. Press Analyze to find similar photos.", warning: false);
            return;
        }

        var shown = Photos.ToHashSet();
        var members = Analysis.Controller.GroupOf(focused).Where(shown.Contains).ToList();
        if (members.Count < 2)
        {
            SetStatus("No similar photos found for this one with the current thresholds (Settings → Analysis).", warning: false);
            return;
        }

        SelectionRequested?.Invoke(this, members);
        SetStatus(
            string.Create(CultureInfo.CurrentCulture, $"Selected a group of {members.Count} similar photos. Press C to compare, I for why they were grouped.")
                + (members.Count > CompareViewModel.MaxPhotos ? string.Create(CultureInfo.CurrentCulture, $" Compare shows the first {CompareViewModel.MaxPhotos}.") : string.Empty),
            warning: false);
    }

    private void UpdateDetailsTarget()
    {
        Analysis.DetailsTile = IsCompareOpen ? Compare.Active?.Tile : IsViewerOpen ? Viewer.Current : SelectedPhoto;
    }

    private void OnInsightsChanged()
    {
        Analysis.RefreshDetails();
        OnPropertyChanged(nameof(CountText));
        if (Filter.Analysis != AnalysisFilterChoice.Any && !IsViewerOpen && !IsCompareOpen)
        {
            RebuildView();
        }
    }

    /// <summary>Whether a tile passes every active filter (decision filters and analysis filter).</summary>
    private bool InView(PhotoTileViewModel tile, LibraryFilter filter) =>
        filter.Matches(tile.Photo, tile.Decision) && Filter.MatchesAnalysis(tile);

    private void OnDecisionsChanged(DecisionChange change)
    {
        UpdateCounts();
        if (IsViewerOpen || IsCompareOpen)
        {
            // These work on a stable snapshot; the grid is rebuilt when they close.
            return;
        }

        var filter = Filter.ToFilter();
        var changed = change.Deltas.Select(d => d.PhotoId).ToHashSet();
        var shown = Photos.Where(t => changed.Contains(t.Id)).ToList();

        // A change that makes a hidden photo match (e.g. undo under a filter) needs a full rebuild to insert it.
        if (_all.Any(t => changed.Contains(t.Id) && InView(t, filter) && !shown.Contains(t)))
        {
            RebuildView();
            return;
        }

        // Otherwise remove photos that no longer match, keeping scroll position and moving selection forward.
        var leaving = shown.Where(t => !InView(t, filter)).ToList();
        if (leaving.Count == 0)
        {
            return;
        }

        var nextIndex = leaving.Select(t => Photos.IndexOf(t)).Min();
        foreach (var tile in leaving)
        {
            Photos.Remove(tile);
        }

        SelectedPhoto = Photos.Count == 0 ? null : Photos[Math.Min(nextIndex, Photos.Count - 1)];
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(CanRecycleRejects));
        OnPropertyChanged(nameof(CountText));
        Export.RefreshCounts();
    }

    /// <summary>Re-applies filter and sort to the whole library (deterministic order).</summary>
    internal void RebuildView()
    {
        var selected = SelectedPhoto;
        IEnumerable<PhotoTileViewModel> view = LibraryQuery
            .Apply(_all, t => t.Photo, t => t.Decision, Filter.ToFilter(), Filter.Sort)
            .Where(Filter.MatchesAnalysis);

        // "In a group" shows each group's members together, groups in a stable order (OrderBy is stable,
        // so the chosen sort still orders photos within each group).
        if (Filter.Analysis == AnalysisFilterChoice.InGroup)
        {
            view = view.OrderBy(t => t.Insight.GroupOrder);
        }

        Photos.ReplaceAll(view);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(CountText));
        SelectedPhoto = selected is not null && Photos.Contains(selected) ? selected : null;
        Locate.UpdateBanner(Filter.Folder?.Path, _all);
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(ShownCountText));
        OnPropertyChanged(nameof(CanRecycleRejects));
        if (Export.IsOpen)
        {
            Export.RefreshCounts();
        }
    }

    // ---------------------------------------------------------------- import

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        var lastFolder = await _settings.GetAsync(AppSettings.ImportLastFolder, cancellationToken).ConfigureAwait(true);
        var folder = _folderPicker.PickFolder("Choose a folder of JPEG photos", lastFolder);
        if (folder is null)
        {
            return;
        }

        await ImportFolderCoreAsync(folder, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Imports a folder dropped on the window (same import as the picker; the Cancel button stops either).</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanImportDropped))]
    private Task ImportDroppedAsync(string folder, CancellationToken cancellationToken) =>
        ImportFolderCoreAsync(folder, cancellationToken);

    private bool CanImportDropped(string folder) => !IsImporting && !string.IsNullOrWhiteSpace(folder);

    partial void OnIsImportingChanged(bool value) => ImportDroppedCommand.NotifyCanExecuteChanged();

    /// <summary>Stops whichever import is running (from the picker or a dropped folder).</summary>
    [RelayCommand]
    private void CancelImport()
    {
        ImportCancelCommand.Execute(null);
        ImportDroppedCancelCommand.Execute(null);
    }

    private async Task ImportFolderCoreAsync(string folder, CancellationToken cancellationToken)
    {
        await SaveSettingAsync(AppSettings.ImportLastFolder, folder).ConfigureAwait(true);

        IsImporting = true;
        ImportProgressValue = 0;
        IsImportProgressIndeterminate = true;
        ImportProgressText = "Scanning folder…";
        SetStatus(null, warning: false);

        var progress = new Progress<ImportProgress>(OnImportProgress);
        try
        {
            var summary = await _importer.ImportFolderAsync(folder, IncludeSubfolders, progress, cancellationToken).ConfigureAwait(true);
            SetStatus(DescribeSummary(summary), warning: summary.Failed > 0 || summary.SkippedFolders.Count > 0 || summary.UnsupportedFiles.Count > 0);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            LogImportFailed(ex, folder);
            SetStatus($"The folder could not be read: {ex.Message}", warning: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogImportFailed(ex, folder);
            SetStatus("Import stopped because of an unexpected error. Photos imported so far were kept. See the log for details.", warning: true);
        }
        finally
        {
            IsImporting = false;
            ImportProgressText = null;
        }

        await ReloadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var photos = await _library.GetPhotosAsync(cancellationToken).ConfigureAwait(true);
            var decisions = await _decisionService.LoadAsync(cancellationToken).ConfigureAwait(true);
            _all = BuildTiles(photos, decisions);
            Decisions.Register(_all);
            await Edits.LoadAsync(_all, cancellationToken).ConfigureAwait(true);
            await Analysis.Controller.LoadAsync(_all, cancellationToken).ConfigureAwait(true);
            ApplyOffline(Locate.Offline); // the last check, until the new one below finishes
        }
        finally
        {
            IsLoading = false;
        }

        await CheckFoldersAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// One tile per photo, except that a RAW + JPEG pair (<see cref="PhotoPairing"/>) is one tile: the JPEG, with the RAW
    /// file as its hidden <see cref="PhotoTileViewModel.Partner"/>. The tile shows the JPEG's decision.
    /// </summary>
    private List<PhotoTileViewModel> BuildTiles(IReadOnlyList<Photo> photos, IReadOnlyDictionary<Guid, PhotoDecision> decisions)
    {
        var pairs = _pairRawJpeg ? PhotoPairing.Find(photos) : PhotoPairs.None;
        var tiles = new List<PhotoTileViewModel>(photos.Count - pairs.Count);
        foreach (var photo in photos)
        {
            if (pairs.IsHiddenRaw(photo.Id))
            {
                continue;
            }

            var partner = pairs.ByCover(photo.Id) is { } pair
                ? new PhotoTileViewModel(pair.Raw, _thumbnails, decisions.GetValueOrDefault(pair.Raw.Id))
                : null;
            tiles.Add(new PhotoTileViewModel(photo, _thumbnails, decisions.GetValueOrDefault(photo.Id)) { Partner = partner });
        }

        return tiles;
    }

    /// <summary>Finds folders that can't be reached (a drive unplugged, a folder moved) and marks their photos Offline.</summary>
    private async Task CheckFoldersAsync(CancellationToken cancellationToken)
    {
        var offline = await Locate.CheckAsync(_all.Select(t => t.Photo.FolderPath), cancellationToken).ConfigureAwait(true);
        ApplyOffline(offline);
    }

    private async Task RecheckFoldersAsync()
    {
        try
        {
            await CheckFoldersAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFolderCheckFailed(ex);
        }
    }

    private void ApplyOffline(IReadOnlyDictionary<string, OfflineFolder> offline)
    {
        foreach (var tile in _all)
        {
            tile.OfflineReason = offline.TryGetValue(tile.Photo.FolderPath, out var o) ? o.Reason : null;
        }

        Filter.SetOffline(offline);
        Filter.SetFolders(_all.Select(t => t.Photo.FolderPath));
        RebuildView();
        UpdateCounts();
    }

    private async Task OnRelocatedAsync(int moved)
    {
        try
        {
            await ReloadAsync(CancellationToken.None).ConfigureAwait(true);
            SetStatus(
                string.Create(CultureInfo.CurrentCulture, $"Moved {moved:N0} photo{(moved == 1 ? string.Empty : "s")} to “{Locate.NewFolder}”. Decisions, edits and analysis came with them."),
                warning: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFolderCheckFailed(ex);
            SetStatus("The photos were moved, but the library could not be reloaded. Restart FrameForge to see them.", warning: true);
        }
    }

    private void OnImportProgress(ImportProgress progress)
    {
        var culture = CultureInfo.CurrentCulture;
        switch (progress.Phase)
        {
            case ImportPhase.Scanning:
                IsImportProgressIndeterminate = true;
                ImportProgressText = "Scanning folder…";
                break;
            case ImportPhase.Reading when progress.Total == 0:
                IsImportProgressIndeterminate = false;
                ImportProgressValue = 1;
                ImportProgressText = string.Create(culture, $"{progress.Discovered:N0} found, all up to date");
                break;
            case ImportPhase.Reading:
                IsImportProgressIndeterminate = false;
                ImportProgressValue = (double)progress.Processed / progress.Total;
                ImportProgressText = string.Create(culture, $"Reading {progress.Processed:N0} / {progress.Total:N0}")
                    + (progress.Failed > 0 ? string.Create(culture, $"  ·  {progress.Failed:N0} unreadable") : string.Empty);
                break;
            case ImportPhase.Finishing:
                IsImportProgressIndeterminate = true;
                ImportProgressText = "Finishing…";
                break;
        }
    }

    internal static string DescribeSummary(ImportSummary summary)
    {
        var culture = CultureInfo.CurrentCulture;
        var folderName = Path.GetFileName(summary.FolderPath);
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = summary.FolderPath;
        }

        var head = summary.WasCancelled
            ? $"Import of “{folderName}” cancelled."
            : $"Imported “{folderName}”.";

        var parts = new List<string>
        {
            string.Create(culture, $"{summary.Added:N0} new"),
            string.Create(culture, $"{summary.Updated:N0} updated"),
            string.Create(culture, $"{summary.Unchanged:N0} unchanged"),
        };

        if (summary.Failed > 0)
        {
            parts.Add(string.Create(culture, $"{summary.Failed:N0} unreadable"));
        }

        if (summary.MarkedMissing > 0)
        {
            parts.Add(string.Create(culture, $"{summary.MarkedMissing:N0} missing"));
        }

        if (summary.MissingCheckSkipped > 0)
        {
            parts.Add(string.Create(culture, $"no photos found, so the {summary.MissingCheckSkipped:N0} already in the library were left as they are"));
        }

        if (summary.SkippedFolders.Count > 0)
        {
            parts.Add(string.Create(culture, $"{summary.SkippedFolders.Count:N0} folder(s) could not be read"));
        }

        if (summary.UnsupportedFiles.Count > 0)
        {
            var total = summary.UnsupportedFiles.Values.Sum();
            var byFormat = string.Join(", ", summary.UnsupportedFiles
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Create(culture, $"{pair.Value:N0} {pair.Key}")));
            parts.Add(string.Create(culture, $"{total:N0} not imported because the format isn't supported ({byFormat})"));
        }

        var tail = summary.WasCancelled ? " Run the import again to continue where it stopped." : string.Empty;
        return $"{head} {string.Join(", ", parts)}.{tail}";
    }

    // ---------------------------------------------------------------- viewer, compare, export

    private bool CanOpenViewer(PhotoTileViewModel? tile) => (tile ?? SelectedPhoto) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenViewer))]
    private void OpenViewer(PhotoTileViewModel? tile)
    {
        tile ??= SelectedPhoto;
        var index = tile is null ? -1 : Photos.IndexOf(tile);
        if (index < 0)
        {
            return;
        }

        SelectedPhoto = tile;
        IsHelpOpen = false;
        Viewer.Open(Photos.ToList(), index); // snapshot: navigation stays stable while deciding
        IsViewerOpen = true;
    }

    [RelayCommand]
    private async Task CloseViewerAsync()
    {
        if (!IsViewerOpen)
        {
            return;
        }

        var current = Viewer.Current;
        await Viewer.CloseAsync().ConfigureAwait(true); // saves a pending Develop change first
        if (!IsViewerOpen)
        {
            return; // closed meanwhile
        }

        IsViewerOpen = false;
        SelectedPhoto = current;
        RebuildView();
        SelectedPhoto ??= FirstAtOrAfter(current);
    }

    [RelayCommand]
    private void OpenCompare()
    {
        var chosen = Photos.Where(_selection.Contains).ToList(); // grid order, not click order
        if (chosen.Count < CompareViewModel.MinPhotos)
        {
            SetStatus("Select 2–4 photos to compare (Ctrl+click or Shift+arrow keys), then press C.", warning: false);
            return;
        }

        if (chosen.Count > CompareViewModel.MaxPhotos)
        {
            SetStatus(string.Create(CultureInfo.CurrentCulture, $"Comparing the first {CompareViewModel.MaxPhotos} of {chosen.Count:N0} selected photos."), warning: false);
            chosen = chosen.Take(CompareViewModel.MaxPhotos).ToList();
        }

        IsHelpOpen = false;
        Compare.Open(chosen);
        IsCompareOpen = true;
    }

    private void CloseCompare()
    {
        if (!IsCompareOpen)
        {
            return;
        }

        var active = Compare.Active?.Tile;
        Compare.Close();
        IsCompareOpen = false;
        SelectedPhoto = active;
        RebuildView();
        SelectedPhoto ??= FirstAtOrAfter(active);
    }

    /// <summary>Opens the preset panel; it applies to the selected photos (or the focused one).</summary>
    [RelayCommand]
    private void OpenPresets()
    {
        IsHelpOpen = false;
        Analysis.IsDetailsOpen = false;
        Presets.Open(DecisionTargets);
    }

    [RelayCommand]
    private void OpenExport() => Export.Open(
        scope => (scope == ExportScope.Keep
                ? LibraryQuery.Apply(_all, t => t.Photo, t => t.Decision, new LibraryFilter { States = new HashSet<CullState> { CullState.Keep } }, Filter.Sort)
                : Photos.ToList())
            // Both files of a pair, JPEG first, each knowing its cover; the panel picks the files for the chosen export.
            .SelectMany(t => t.Partner is { } raw
                ? new[] { new ExportItem(t.Photo, t.Decision, t.Recipe) { Cover = t.Photo }, new ExportItem(raw.Photo, t.Decision, t.Recipe) { Cover = t.Photo } }
                : [new ExportItem(t.Photo, t.Decision, t.Recipe)])
            .ToList(),
        _all.Select(t => t.Photo.FolderPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    /// <summary>
    /// Opens the "Move rejects to Recycle Bin" panel for the rejects shown (a chosen folder narrows them). It only
    /// checks the files; nothing moves until the panel's button is pressed. Grid and Rejected view only.
    /// </summary>
    [RelayCommand]
    private void OpenRecycleRejects()
    {
        if (IsViewerOpen || IsCompareOpen)
        {
            return;
        }

        if (Filter.SelectedScope.Scope != LibraryScope.Rejected)
        {
            SetStatus("To move rejects to the Recycle Bin, open the Rejected view first.", warning: false);
            return;
        }

        // Only photos still marked Reject, whatever else the toolbar filters show.
        var rejects = Photos.Where(t => t.Decision.State == CullState.Reject).ToList();
        if (rejects.Count == 0)
        {
            SetStatus("There are no rejected photos here.", warning: false);
            return;
        }

        IsHelpOpen = false;
        Analysis.IsDetailsOpen = false;
        if (Export.IsOpen)
        {
            Export.CloseCommand.Execute(null);
        }

        if (Presets.IsOpen)
        {
            Presets.CloseCommand.Execute(null);
        }

        _ = Recycle.OpenAsync(rejects);
    }

    private void OnRecycleCompleted(RecycleCompleted completed) => _ = OnRecycleCompletedAsync(completed);

    /// <summary>
    /// After a move: the photos whose files went to the Recycle Bin leave the library, with their decisions, edits and
    /// analysis, and the status line says what happened. A file that wasn't moved keeps its photo (a pair whose RAW
    /// file stayed becomes that RAW file on its own, still marked Reject).
    /// </summary>
    private async Task OnRecycleCompletedAsync(RecycleCompleted completed)
    {
        var moved = completed.Summary.Files.Where(f => f.Outcome == RecycleOutcome.Moved).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var photos = completed.Photos.SelectMany(t => t.Partner is { } raw ? new[] { t, raw } : [t]).ToList();
        var removed = photos.Where(t => moved.Contains(t.FilePath)).Select(t => t.Id).ToHashSet();
        var frames = completed.Photos.Count(t => moved.Contains(t.FilePath) || (t.Partner is { } raw && moved.Contains(raw.FilePath)));
        var text = RecycleResultText(completed, frames);
        var warning = completed.Summary.StoppedForSafety || completed.Summary.Cancelled || completed.NotSent.Count > 0
            || completed.Summary.Files.Any(f => f.Outcome is RecycleOutcome.Skipped or RecycleOutcome.Failed);

        if (removed.Count > 0)
        {
            try
            {
                await _library.RemoveAsync(removed, CancellationToken.None).ConfigureAwait(true);
                Decisions.Forget(removed);
                await ReloadAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRemoveFailed(ex, removed.Count);
                SetStatus(text + " The library could not be updated; restart FrameForge to see the change.", warning: true);
                return;
            }
        }

        SetStatus(text, warning);
    }

    /// <summary>"Moved 48 files (31 photos) to the Recycle Bin. 3 not moved (3 no Recycle Bin on the drive). The log lists every file."</summary>
    internal static string RecycleResultText(RecycleCompleted completed, int photos)
    {
        var culture = CultureInfo.CurrentCulture;
        var summary = completed.Summary;
        var files = summary.Moved;
        var text = files == photos
            ? string.Create(culture, $"Moved {files:N0} {(files == 1 ? "photo" : "photos")} to the Recycle Bin.")
            : string.Create(culture, $"Moved {files:N0} {(files == 1 ? "file" : "files")} ({photos:N0} {(photos == 1 ? "photo" : "photos")}) to the Recycle Bin.");

        // Why the others stayed: refused at the check, refused just before the move, or left in place by Windows.
        var reasons = completed.NotSent.Select(c => RecycleRejectsViewModel.ShortReason(c.Refusal))
            .Concat(summary.Files.Where(f => f.Outcome == RecycleOutcome.Skipped).Select(f => RecycleRejectsViewModel.ShortReason(f.Refusal)))
            .Concat(summary.Files.Where(f => f.Outcome == RecycleOutcome.Failed).Select(_ => "in use or not allowed"))
            .GroupBy(r => r)
            .Select(g => string.Create(culture, $"{g.Count():N0} {g.Key}"))
            .ToList();
        var notMoved = completed.NotSent.Count + summary.Skipped + summary.Failed;
        if (notMoved > 0)
        {
            text += string.Create(culture, $" {notMoved:N0} not moved ({string.Join(", ", reasons)}).");
        }

        if (summary.StoppedForSafety)
        {
            text += " Stopped: a file left its folder without reaching the Recycle Bin, so nothing else was moved. Check the Recycle Bin.";
        }
        else if (summary.Cancelled)
        {
            text += " Stopped on request; the rest were not moved.";
        }

        return text + " The log lists every file.";
    }

    /// <summary>After a rebuild hides the previously current photo, select the photo that now occupies its place.</summary>
    private PhotoTileViewModel? FirstAtOrAfter(PhotoTileViewModel? tile)
    {
        if (tile is null || Photos.Count == 0)
        {
            return null;
        }

        var fullOrder = LibraryQuery.Apply(_all, t => t.Photo, t => t.Decision, LibraryFilter.All, Filter.Sort);
        var positionOf = new Dictionary<PhotoTileViewModel, int>(fullOrder.Count);
        for (var i = 0; i < fullOrder.Count; i++)
        {
            positionOf[fullOrder[i]] = i;
        }

        var position = positionOf.GetValueOrDefault(tile, -1);
        return Photos.FirstOrDefault(t => positionOf[t] >= position) ?? Photos[^1];
    }

    private void SetStatus(string? message, bool warning)
    {
        StatusMessage = message;
        StatusIsWarning = warning;
    }

    private async Task SaveSettingAsync<T>(SettingDefinition<T> setting, T value)
    {
        try
        {
            await _settings.SetAsync(setting, value).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSettingSaveFailed(ex, setting.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the library failed.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking whether library folders can be reached failed.")]
    private partial void LogFolderCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Removing {Count} photo(s) moved to the Recycle Bin from the library failed.")]
    private partial void LogRemoveFailed(Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import of {FolderPath} failed.")]
    private partial void LogImportFailed(Exception exception, string folderPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving setting {SettingKey} failed.")]
    private partial void LogSettingSaveFailed(Exception exception, string settingKey);
}
