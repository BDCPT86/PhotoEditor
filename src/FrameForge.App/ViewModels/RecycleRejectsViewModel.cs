using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Cleanup;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>One line of the panel: a folder the files come from, or a reason some files will be skipped.</summary>
/// <param name="Text">"Shoot — 31 photos" or "3 files on a drive without a Recycle Bin".</param>
/// <param name="Hint">The folder's full path, or up to three of the skipped files' names.</param>
public sealed record RecycleLine(string Text, string? Hint);

/// <summary>What was moved, for the library to drop those photos (M09 B3).</summary>
/// <param name="Summary">Every file's result.</param>
/// <param name="Photos">The photos (frames) the panel was opened for.</param>
public sealed record RecycleCompleted(RecycleSummary Summary, IReadOnlyList<PhotoTileViewModel> Photos);

/// <summary>
/// The "Move rejects to Recycle Bin" side panel (M09 B2). Opening it only checks the files; nothing moves until
/// <see cref="ConfirmCommand"/>. A RAW + JPEG pair counts as one photo and both of its files go.
/// </summary>
public sealed partial class RecycleRejectsViewModel(IRecycleBin recycleBin, ILogger<RecycleRejectsViewModel> logger) : ObservableObject
{
    private IReadOnlyList<PhotoTileViewModel> _photos = [];
    private IReadOnlyList<RecycleCheck> _checks = [];
    private CancellationTokenSource? _cts;
    private int _openGeneration;

    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Checking the files' drives and sizes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(CloseCommand))]
    private bool _isMoving;

    /// <summary>"31 photos · 48 files · 1.2 GB" — what will move.</summary>
    [ObservableProperty]
    private string? _summaryText;

    /// <summary>The primary button: "Move 48 files to Recycle Bin".</summary>
    [ObservableProperty]
    private string _confirmText = "Move to Recycle Bin";

    /// <summary>Why nothing can move, or a check that failed; null when the panel is ready.</summary>
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _progressText;

    /// <summary>Where the photos are: one line per folder.</summary>
    public ObservableCollection<RecycleLine> Folders { get; } = [];

    /// <summary>Files that will be skipped: one line per reason. Empty when all can go.</summary>
    public ObservableCollection<RecycleLine> Skipped { get; } = [];

    public bool HasSkipped => Skipped.Count > 0;

    /// <summary>The files that will move.</summary>
    public int MovableCount => _checks.Count(c => c.CanMove);

    /// <summary>Raised after a move finished (completed, cancelled or stopped for safety).</summary>
    public event EventHandler<RecycleCompleted>? Completed;

    /// <summary>Opens the panel for these photos (the rejects shown) and checks their files. Nothing moves.</summary>
    public async Task OpenAsync(IReadOnlyList<PhotoTileViewModel> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var generation = ++_openGeneration;
        _photos = photos;
        _checks = [];
        Folders.Clear();
        Skipped.Clear();
        OnPropertyChanged(nameof(HasSkipped));
        SummaryText = null;
        Message = null;
        ProgressText = null;
        ProgressValue = 0;
        ConfirmText = "Move to Recycle Bin";
        IsOpen = true;
        IsChecking = true;

        try
        {
            var checks = await recycleBin.CheckAsync(FilesOf(photos), CancellationToken.None).ConfigureAwait(true);
            if (generation != _openGeneration)
            {
                return; // opened again meanwhile
            }

            _checks = checks;
            ShowChecks();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCheckFailed(ex);
            Message = "The files couldn't be checked, so nothing will move. See the log for details.";
        }
        finally
        {
            if (generation == _openGeneration)
            {
                IsChecking = false;
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Both files of a pair, JPEG first; every other photo's own file.</summary>
    internal static List<string> FilesOf(IEnumerable<PhotoTileViewModel> photos) =>
        photos.SelectMany(p => p.Partner is { } raw ? new[] { p.FilePath, raw.FilePath } : [p.FilePath]).ToList();

    private void ShowChecks()
    {
        var culture = CultureInfo.CurrentCulture;
        var movable = _checks.Where(c => c.CanMove).ToList();
        var movablePaths = movable.Select(c => c.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var photos = _photos.Count(p => movablePaths.Contains(p.FilePath) || (p.Partner is { } raw && movablePaths.Contains(raw.FilePath)));
        var bytes = movable.Sum(c => c.Size);

        SummaryText = movable.Count == 0
            ? null
            : movable.Count == photos
                ? string.Create(culture, $"{photos:N0} {Plural(photos, "photo")}  ·  {FormatSize(bytes)}")
                : string.Create(culture, $"{photos:N0} {Plural(photos, "photo")}  ·  {movable.Count:N0} files  ·  {FormatSize(bytes)}");
        ConfirmText = movable.Count == 0
            ? "Move to Recycle Bin"
            : string.Create(culture, $"Move {movable.Count:N0} {Plural(movable.Count, "file")} to Recycle Bin");

        foreach (var folder in movable
            .GroupBy(c => Path.GetDirectoryName(c.Path) ?? c.Path, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var count = folder.Count();
            Folders.Add(new RecycleLine(
                string.Create(culture, $"{Path.GetFileName(folder.Key.TrimEnd('\\', '/'))}  —  {count:N0} {Plural(count, "file")}"),
                folder.Key));
        }

        foreach (var reason in _checks.Where(c => !c.CanMove).GroupBy(c => c.Refusal).OrderBy(g => g.Key))
        {
            var names = reason.Select(c => Path.GetFileName(c.Path)).ToList();
            var shown = string.Join(", ", names.Take(3)) + (names.Count > 3 ? string.Create(culture, $" and {names.Count - 3:N0} more") : string.Empty);
            Skipped.Add(new RecycleLine(string.Create(culture, $"{names.Count:N0} {Plural(names.Count, "file")} {Describe(reason.Key)}"), shown));
        }

        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(MovableCount));
        if (movable.Count == 0)
        {
            Message = _checks.Count == 0
                ? "There are no rejected photos here."
                : "None of these files can go to the Recycle Bin, so nothing will move. They stay where they are, still marked Reject.";
        }
    }

    /// <summary>Why files are skipped, finishing "3 files …" (no verb, so it reads for one file or many).</summary>
    internal static string Describe(RecycleRefusal refusal) => refusal switch
    {
        RecycleRefusal.Missing => "not found",
        RecycleRefusal.NoRecycleBinOnDrive => "on a drive without a Recycle Bin (memory card, USB drive or network)",
        RecycleRefusal.DriveUnavailable => "on a drive that can't be reached",
        RecycleRefusal.RecycleBinOff => "on a drive with its Recycle Bin turned off",
        RecycleRefusal.RecycleBinUnknown => "on a drive where the Recycle Bin can't be confirmed",
        RecycleRefusal.TooLarge => "too large for the Recycle Bin",
        _ => "that can't be moved",
    };

    private bool CanConfirm() => !IsChecking && !IsMoving && _checks.Any(c => c.CanMove);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var paths = _checks.Where(c => c.CanMove).Select(c => c.Path).ToList();
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsMoving = true;
        Message = null;
        ProgressValue = 0;
        ProgressText = "Starting…";
        try
        {
            var progress = new Progress<RecycleProgress>(p =>
            {
                ProgressValue = p.Total == 0 ? 1 : (double)p.Done / p.Total;
                ProgressText = string.Create(CultureInfo.CurrentCulture, $"{p.Done:N0} / {p.Total:N0}  ·  {p.CurrentFile}");
            });
            var summary = await recycleBin.MoveAsync(paths, progress, cts.Token).ConfigureAwait(true);
            IsOpen = false;
            Completed?.Invoke(this, new RecycleCompleted(summary, _photos));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMoveFailed(ex);
            Message = "Moving stopped with an error. See the log for which files moved.";
        }
        finally
        {
            _cts = null;
            IsMoving = false;
            ProgressText = null;
        }
    }

    /// <summary>Stops after the file being moved; the rest stay where they are.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanClose() => !IsMoving;

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        _openGeneration++;
        IsOpen = false;
        IsChecking = false;
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):0.0} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):0} MB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, bytes / 1024):0} KB"),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking files before moving them to the Recycle Bin failed.")]
    private partial void LogCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Moving files to the Recycle Bin failed.")]
    private partial void LogMoveFailed(Exception exception);
}
