using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

public enum ExportScope
{
    /// <summary>Every photo marked Keep, regardless of the current filter.</summary>
    Keep,

    /// <summary>The photos currently shown in the grid.</summary>
    CurrentView,
}

public enum ExportMode
{
    CopyFiles,
    Manifest,

    /// <summary>Render each photo's edit into a new JPEG.</summary>
    EditedJpegs,
}

/// <summary>Non-modal export panel: choose scope and mode, then a destination; shows progress and a summary.</summary>
public sealed partial class ExportViewModel(
    IExportService exporter,
    IEditedExportService editedExporter,
    IFolderPicker folderPicker,
    IFileSavePicker savePicker,
    ILogger<ExportViewModel> logger) : ObservableObject
{
    /// <summary>JPEG quality for edited exports.</summary>
    [ObservableProperty]
    private int _quality = EditedExportOptions.Default.Quality;

    /// <summary>Optional longest side for edited exports; empty keeps full size.</summary>
    [ObservableProperty]
    private string _maxLongSideText = string.Empty;

    [ObservableProperty]
    private bool _keepMetadata = true;

    private Func<ExportScope, IReadOnlyList<ExportItem>> _source = _ => [];
    private IReadOnlyCollection<string> _libraryFolders = [];
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private ExportScope _scope = ExportScope.Keep;

    [ObservableProperty]
    private ExportMode _mode = ExportMode.CopyFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _keepCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _viewCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(ConfirmPendingCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private bool _resultIsWarning;

    /// <summary>Destination awaiting confirmation because it is inside an imported folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDestination))]
    private string? _pendingDestination;

    public bool HasPendingDestination => PendingDestination is not null;

    public string CountText => string.Create(CultureInfo.CurrentCulture, $"{(Scope == ExportScope.Keep ? KeepCount : ViewCount):N0} photo(s)");

    public void Open(Func<ExportScope, IReadOnlyList<ExportItem>> source, IReadOnlyCollection<string> libraryFolders)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(libraryFolders);
        _source = source;
        _libraryFolders = libraryFolders;
        RefreshCounts();
        PendingDestination = null;
        ResultText = null;
        IsOpen = true;
    }

    public void RefreshCounts()
    {
        KeepCount = _source(ExportScope.Keep).Count;
        ViewCount = _source(ExportScope.CurrentView).Count;
    }

    [RelayCommand]
    private void Close()
    {
        if (!IsExporting)
        {
            IsOpen = false;
            PendingDestination = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanStart() => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        PendingDestination = null;
        ResultText = null; // a new export starts clean; the previous result no longer applies
        var items = _source(Scope);
        if (items.Count == 0)
        {
            SetResult(Scope == ExportScope.Keep ? "No photos are marked Keep (press P to keep a photo)." : "No photos are shown.", warning: true);
            return;
        }

        if (Mode == ExportMode.Manifest)
        {
            var path = savePicker.PickSaveFile("Save selection manifest", "frameforge-selection.csv", "CSV file (*.csv)|*.csv");
            if (path is not null)
            {
                await RunAsync(items, path, manifest: true).ConfigureAwait(true);
            }

            return;
        }

        if (Mode == ExportMode.EditedJpegs && EditedOptions() is null)
        {
            SetResult($"The longest side must be a whole number of pixels (64–65,000), or empty for full size.", warning: true);
            return;
        }

        var folder = folderPicker.PickFolder(Mode == ExportMode.EditedJpegs ? "Choose a folder for the edited JPEGs" : "Choose a folder to copy photos into", null);
        if (folder is null)
        {
            return;
        }

        if (_libraryFolders.Any(f => FolderScope.IsSameOrUnder(folder, f) || FolderScope.IsSameOrUnder(f, folder)))
        {
            // Copies placed in an imported folder would appear as new photos on the next import.
            PendingDestination = folder;
            return;
        }

        await RunAsync(items, folder, manifest: false).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ConfirmPendingAsync()
    {
        if (PendingDestination is not { } folder)
        {
            return;
        }

        PendingDestination = null;
        await RunAsync(_source(Scope), folder, manifest: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private void DiscardPending() => PendingDestination = null;

    private async Task RunAsync(IReadOnlyList<ExportItem> items, string destination, bool manifest)
    {
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsExporting = true;
        ResultText = null;
        ProgressValue = 0;
        ProgressText = manifest ? "Writing manifest…" : "Starting…";

        try
        {
            var progress = new Progress<ExportProgress>(OnProgress);
            ExportSummary summary;
            if (manifest)
            {
                summary = await exporter.WriteManifestAsync(items, destination, cts.Token).ConfigureAwait(true);
            }
            else if (Mode == ExportMode.EditedJpegs && EditedOptions() is { } options)
            {
                var edited = items.Select(i => new EditedExportItem(i.Photo, i.Recipe ?? Core.Editing.EditRecipe.Neutral)).ToList();
                summary = await editedExporter.ExportAsync(edited, destination, options, progress, cts.Token).ConfigureAwait(true);
            }
            else
            {
                summary = await exporter.CopyAsync(items, destination, progress, cts.Token).ConfigureAwait(true);
            }

            SetResult(Describe(summary, manifest, Mode == ExportMode.EditedJpegs), warning: summary.Failures.Count > 0 || summary.WasCancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogExportFailed(ex, destination);
            SetResult($"Export failed: {ex.Message}", warning: true);
        }
        catch (OperationCanceledException)
        {
            SetResult("Export cancelled.", warning: true);
        }
        finally
        {
            _cts = null;
            IsExporting = false;
            ProgressText = null;
        }
    }

    private void OnProgress(ExportProgress progress)
    {
        ProgressValue = progress.Total == 0 ? 1 : (double)progress.Completed / progress.Total;
        ProgressText = string.Create(
            CultureInfo.CurrentCulture,
            $"{progress.Completed:N0} / {progress.Total:N0}  ·  {progress.BytesCopied / (1024.0 * 1024.0):N0} MB");
    }

    /// <summary>Options for an edited export, or null when the long-side text is not valid.</summary>
    internal EditedExportOptions? EditedOptions()
    {
        int? maxSide = null;
        if (!string.IsNullOrWhiteSpace(MaxLongSideText))
        {
            if (!int.TryParse(MaxLongSideText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
            {
                return null;
            }

            maxSide = parsed;
        }

        var options = new EditedExportOptions
        {
            Quality = Math.Clamp(Quality, EditedExportOptions.MinQuality, EditedExportOptions.MaxQuality),
            MaxLongSide = maxSide,
            KeepMetadata = KeepMetadata,
        };
        return options.Validate().Count == 0 ? options : null;
    }

    internal static string Describe(ExportSummary summary, bool manifest, bool edited = false)
    {
        var culture = CultureInfo.CurrentCulture;
        if (manifest)
        {
            return string.Create(culture, $"Manifest of {summary.Exported:N0} photo(s) saved to {summary.Destination}.");
        }

        var verb = edited ? "saved as edited JPEGs" : "copied";
        var text = summary.WasCancelled
            ? string.Create(culture, $"Export cancelled: {summary.Exported:N0} of {summary.Requested:N0} {verb} to {summary.Destination}.")
            : edited
                ? string.Create(culture, $"Saved {summary.Exported:N0} edited JPEG(s) to {summary.Destination}.")
                : string.Create(culture, $"Copied {summary.Exported:N0} photo(s) to {summary.Destination}.");

        if (summary.Renamed > 0)
        {
            text += string.Create(culture, $" {summary.Renamed:N0} renamed to avoid replacing existing files.");
        }

        if (summary.Failures.Count > 0)
        {
            text += string.Create(culture, $" {summary.Failures.Count:N0} could not be copied (see the log): ")
                + string.Join(", ", summary.Failures.Take(3).Select(f => Path.GetFileName(f.SourcePath)))
                + (summary.Failures.Count > 3 ? "…" : string.Empty);
        }

        return text;
    }

    private void SetResult(string text, bool warning)
    {
        ResultText = text;
        ResultIsWarning = warning;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Export to {Destination} failed.")]
    private partial void LogExportFailed(Exception exception, string destination);
}
