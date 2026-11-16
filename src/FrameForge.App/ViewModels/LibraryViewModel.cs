using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Photos;
using FrameForge.Application.Settings;
using FrameForge.Core.Photos;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>Photo grid, folder import, and the entry point to the single-photo viewer.</summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    private readonly IPhotoLibraryService _library;
    private readonly IPhotoImportService _importer;
    private readonly ISettingsService _settings;
    private readonly IFolderPicker _folderPicker;
    private readonly IThumbnailSource _thumbnails;
    private readonly ILogger<LibraryViewModel> _logger;
    private bool _loaded;

    public LibraryViewModel(
        IPhotoLibraryService library,
        IPhotoImportService importer,
        ISettingsService settings,
        IFolderPicker folderPicker,
        IThumbnailSource thumbnails,
        ViewerViewModel viewer,
        ILogger<LibraryViewModel> logger)
    {
        _library = library;
        _importer = importer;
        _settings = settings;
        _folderPicker = folderPicker;
        _thumbnails = thumbnails;
        _logger = logger;
        Viewer = viewer;
        Viewer.CloseRequested += (_, _) => CloseViewer();
    }

    public override string Title => "Library";

    public ViewerViewModel Viewer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(CountText))]
    private IReadOnlyList<PhotoTileViewModel> _photos = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenViewerCommand))]
    private PhotoTileViewModel? _selectedPhoto;

    [ObservableProperty]
    private bool _includeSubfolders = true;

    [ObservableProperty]
    private bool _isViewerOpen;

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

    public bool IsEmpty => Photos.Count == 0 && !IsLoading && !IsImporting;

    public string CountText
    {
        get
        {
            if (Photos.Count == 0)
            {
                return string.Empty;
            }

            var failed = Photos.Count(p => p.Status == ImportStatus.Failed);
            var missing = Photos.Count(p => p.Status == ImportStatus.Missing);
            var text = string.Create(CultureInfo.CurrentCulture, $"{Photos.Count:N0} photos");
            if (failed > 0)
            {
                text += string.Create(CultureInfo.CurrentCulture, $"  ·  {failed:N0} unreadable");
            }

            if (missing > 0)
            {
                text += string.Create(CultureInfo.CurrentCulture, $"  ·  {missing:N0} missing");
            }

            return text;
        }
    }

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            IncludeSubfolders = await _settings.GetAsync(AppSettings.ImportIncludeSubfolders, cancellationToken).ConfigureAwait(true);
            await ReloadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loaded = false;
            LogLoadFailed(ex);
            SetStatus("The library could not be loaded. See the log for details.", warning: true);
        }
    }

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        if (_loaded)
        {
            _ = SaveSettingAsync(AppSettings.ImportIncludeSubfolders, value);
        }
    }

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

    private bool CanOpenViewer(PhotoTileViewModel? tile) => (tile ?? SelectedPhoto) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenViewer))]
    private void OpenViewer(PhotoTileViewModel? tile)
    {
        tile ??= SelectedPhoto;
        if (tile is null)
        {
            return;
        }

        var index = IndexOf(tile);
        if (index < 0)
        {
            return;
        }

        SelectedPhoto = tile;
        Viewer.Open(Photos, index);
        IsViewerOpen = true;
    }

    [RelayCommand]
    private void CloseViewer()
    {
        if (!IsViewerOpen)
        {
            return;
        }

        SelectedPhoto = Viewer.Current ?? SelectedPhoto;
        Viewer.Close();
        IsViewerOpen = false;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var selectedId = SelectedPhoto?.Id;
            var photos = await _library.GetPhotosAsync(cancellationToken).ConfigureAwait(true);
            var tiles = photos.Select(p => new PhotoTileViewModel(p, _thumbnails)).ToList();

            // Replace the whole list at once: one notification instead of thousands.
            Photos = tiles;
            SelectedPhoto = selectedId is { } id ? tiles.Find(t => t.Id == id) : null;
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

    private int IndexOf(PhotoTileViewModel tile)
    {
        for (var i = 0; i < Photos.Count; i++)
        {
            if (ReferenceEquals(Photos[i], tile))
            {
                return i;
            }
        }

        return -1;
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
