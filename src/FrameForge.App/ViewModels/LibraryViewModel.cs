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
        ILogger<LibraryViewModel> logger)
    {
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

        Viewer.CloseRequested += (_, _) => CloseViewer();
        Compare.CloseRequested += (_, _) => CloseCompare();
        Filter.Changed += (_, _) => RebuildView();
        Decisions.DecisionsChanged += (_, change) => OnDecisionsChanged(change);
    }

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

    [ObservableProperty]
    private bool _isHelpOpen;

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
            // Settings may have changed while on another page.
            _autoAdvance = await _settings.GetAsync(AppSettings.ViewerAutoAdvance, cancellationToken).ConfigureAwait(true);
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

    void ISelectedItemsSink.UpdateSelection(IReadOnlyList<object> items) =>
        _selection = items.OfType<PhotoTileViewModel>().ToList();

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
            case CullingAction.Undo:
                _ = ReportAsync(Decisions.UndoAsync(), "Undid");
                return true;
            case CullingAction.Redo:
                _ = ReportAsync(Decisions.RedoAsync(), "Redid");
                return true;
            case CullingAction.Compare:
                OpenCompare();
                return true;
            case CullingAction.Refresh:
                RebuildView();
                return true;
            case CullingAction.ToggleHelp:
                IsHelpOpen = !IsHelpOpen;
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
        if (_all.Any(t => changed.Contains(t.Id) && filter.Matches(t.Photo, t.Decision) && !shown.Contains(t)))
        {
            RebuildView();
            return;
        }

        // Otherwise remove photos that no longer match, keeping scroll position and moving selection forward.
        var leaving = shown.Where(t => !filter.Matches(t.Photo, t.Decision)).ToList();
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
        var view = LibraryQuery.Apply(_all, t => t.Photo, t => t.Decision, Filter.ToFilter(), Filter.Sort);
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
    private void CloseViewer()
    {
        if (!IsViewerOpen)
        {
            return;
        }

        var current = Viewer.Current;
        Viewer.Close();
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

    [RelayCommand]
    private void OpenExport() => Export.Open(
        scope => (scope == ExportScope.Keep
                ? LibraryQuery.Apply(_all, t => t.Photo, t => t.Decision, new LibraryFilter { States = new HashSet<CullState> { CullState.Keep } }, Filter.Sort)
                : Photos.ToList())
            .Select(t => new ExportItem(t.Photo, t.Decision))
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
