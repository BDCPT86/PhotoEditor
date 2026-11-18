using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Controls;
using FrameForge.App.Services;
using FrameForge.App.Shortcuts;
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
        ILogger<LibraryViewModel> logger)
    {
        Analysis = analysis;
        Presets = presets;
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

    /// <summary>The photos shown in the grid: the library after filter and sort.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsFilteredEmpty), nameof(CountText))]
    private ObservableCollection<PhotoTileViewModel> _photos = [];

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
            int keep = 0, review = 0, reject = 0, problems = 0;
            foreach (var tile in _all)
            {
                switch (tile.Decision.State)
                {
                    case CullState.Keep: keep++; break;
                    case CullState.Review: review++; break;
                    case CullState.Reject: reject++; break;
                }

                if (tile.Status != ImportStatus.Imported)
                {
                    problems++;
                }
            }

            var shown = Photos.Count == _all.Count
                ? string.Create(culture, $"{_all.Count:N0} photos")
                : string.Create(culture, $"{Photos.Count:N0} of {_all.Count:N0} shown");
            var text = string.Create(culture, $"{shown}  ·  {keep:N0} keep  ·  {review:N0} review  ·  {reject:N0} reject");
            return problems > 0 ? text + string.Create(culture, $"  ·  {problems:N0} problem(s)") : text;
        }
    }

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            // Settings may have changed while on another page: thresholds re-evaluate instantly from cached results.
            _autoAdvance = await _settings.GetAsync(AppSettings.ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);
            await Analysis.Controller.RefreshSettingsAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        _loaded = true;
        try
        {
            IncludeSubfolders = await _settings.GetAsync(AppSettings.ImportIncludeSubfolders, cancellationToken).ConfigureAwait(true);
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
            case CullingAction.Refresh:
                RebuildView();
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
            var count = change.Deltas.Count;
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
        OnPropertyChanged(nameof(CountText));
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

        Photos = new ObservableCollection<PhotoTileViewModel>(view);
        SelectedPhoto = selected is not null && Photos.Contains(selected) ? selected : null;
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
            SetStatus(DescribeSummary(summary), warning: summary.Failed > 0 || summary.SkippedFolders.Count > 0);
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
            _all = photos
                .Select(p => new PhotoTileViewModel(p, _thumbnails, decisions.GetValueOrDefault(p.Id)))
                .ToList();
            Decisions.Register(_all);
            await Edits.LoadAsync(_all, cancellationToken).ConfigureAwait(true);
            await Analysis.Controller.LoadAsync(_all, cancellationToken).ConfigureAwait(true);
            RebuildView();
        }
        finally
        {
            IsLoading = false;
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

        if (summary.SkippedFolders.Count > 0)
        {
            parts.Add(string.Create(culture, $"{summary.SkippedFolders.Count:N0} folder(s) could not be read"));
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
            .Select(t => new ExportItem(t.Photo, t.Decision, t.Recipe))
            .ToList(),
        _all.Select(t => t.Photo.FolderPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Import of {FolderPath} failed.")]
    private partial void LogImportFailed(Exception exception, string folderPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving setting {SettingKey} failed.")]
    private partial void LogSettingSaveFailed(Exception exception, string settingKey);
}
