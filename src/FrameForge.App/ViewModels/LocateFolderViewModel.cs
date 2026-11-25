using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Photos;
using FrameForge.Core.Library;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>One line of a relocation preview, e.g. "1,009 found".</summary>
public sealed record LocateCountLine(string Text, string? Hint, bool IsWarning);

/// <summary>
/// Folders that can't be reached (Offline), and the Locate folder… flow that points a moved folder's photos at its
/// new place. The library owns the photos; this raises <see cref="Relocated"/> so it can reload.
/// </summary>
public sealed partial class LocateFolderViewModel(
    IFolderRelocationService relocation,
    FolderAvailabilityChecker availability,
    IFolderPicker folderPicker,
    ILogger<LocateFolderViewModel> logger) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _work;
    private RelocationPlan? _plan;

    /// <summary>Raised after photos were moved, so the library reloads.</summary>
    public event EventHandler<int>? Relocated;

    /// <summary>Folders that can't be reached right now, from the last check.</summary>
    public IReadOnlyDictionary<string, OfflineFolder> Offline { get; private set; } = new Dictionary<string, OfflineFolder>();

    /// <summary>Checks every library folder. Never throws for an unreachable drive; it just counts as offline.</summary>
    public async Task<IReadOnlyDictionary<string, OfflineFolder>> CheckAsync(IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        Offline = await availability.CheckAsync(folders, cancellationToken).ConfigureAwait(true);
        if (Offline.Count > 0)
        {
            var reasons = string.Join("; ", Offline.Values.Select(o => o.Reason).Distinct());
            LogOffline(Offline.Count, reasons);
        }

        return Offline;
    }

    // ------------------------------------------------------------------ banner over the grid

    /// <summary>Offline or missing photos where the user is looking, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBanner))]
    private string? _bannerText;

    /// <summary>The folder Locate folder… would start from, or null when there's no single one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LocateBannerFolderCommand))]
    [NotifyPropertyChangedFor(nameof(HasBannerFolder))]
    private string? _bannerFolder;

    public bool HasBanner => BannerText is not null;

    public bool HasBannerFolder => BannerFolder is not null;

    /// <summary>Works out the banner for the folder shown (null = all folders).</summary>
    public void UpdateBanner(string? shownFolder, IReadOnlyList<PhotoTileViewModel> library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var culture = CultureInfo.CurrentCulture;
        var inView = shownFolder is null ? library : library.Where(t => LibraryFilter.IsInFolder(t.Photo.FolderPath, shownFolder)).ToList();
        var offline = inView.Where(t => t.OfflineReason is not null).ToList();
        var missing = inView.Count(t => t.OfflineReason is null && t.Status == ImportStatus.Missing);

        if (offline.Count > 0)
        {
            var roots = TopFolders(offline.Select(t => t.Photo.FolderPath));
            var reasons = offline.Select(t => t.OfflineReason!).Distinct().ToList();
            var head = reasons.Count == 1 ? reasons[0] : string.Create(culture, $"{roots.Count:N0} folders can't be reached.");
            BannerText = string.Create(culture, $"{head} {offline.Count:N0} photo{(offline.Count == 1 ? " is" : "s are")} offline: your decisions and edits are kept, and thumbnails already made still show.");
            BannerFolder = roots.Count == 1 ? roots[0] : null;
        }
        else if (missing > 0 && shownFolder is not null)
        {
            BannerText = string.Create(culture, $"{missing:N0} photo{(missing == 1 ? " is" : "s are")} missing from this folder. If the folder was moved, Locate folder… finds them again.");
            BannerFolder = shownFolder;
        }
        else
        {
            BannerText = null;
            BannerFolder = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLocateBannerFolder))]
    private Task LocateBannerFolderAsync() => BeginAsync(BannerFolder);

    private bool CanLocateBannerFolder() => BannerFolder is not null && !IsBusy;

    // ------------------------------------------------------------------ the Locate panel

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ChooseAgainCommand), nameof(LocateBannerFolderCommand), nameof(BeginCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _oldFolder;

    [ObservableProperty]
    private string? _newFolder;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isMessageWarning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private int _movable;

    public string ApplyText => Movable == 1 ? "Move 1 photo" : string.Create(CultureInfo.CurrentCulture, $"Move {Movable:N0} photos");

    partial void OnMovableChanged(int value) => OnPropertyChanged(nameof(ApplyText));

    public System.Collections.ObjectModel.ObservableCollection<LocateCountLine> Lines { get; } = [];

    /// <summary>Starts Locate folder… for a library folder: asks where it is now, then previews.</summary>
    [RelayCommand(CanExecute = nameof(CanBegin))]
    private async Task BeginAsync(string? folder)
    {
        if (folder is null)
        {
            return;
        }

        OldFolder = folder;
        NewFolder = null;
        _plan = null;
        Movable = 0;
        Lines.Clear();
        Message = null;
        IsOpen = true;
        await ChooseAgainAsync().ConfigureAwait(true);
    }

    private bool CanBegin(string? folder) => folder is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChooseAgain))]
    private async Task ChooseAgainAsync()
    {
        if (OldFolder is null)
        {
            return;
        }

        // No starting folder: the old one may be on a drive that is gone, and probing it here could block.
        var picked = folderPicker.PickFolder($"Where is “{Path.GetFileName(OldFolder)}” now?", null);
        if (picked is null)
        {
            if (_plan is null)
            {
                IsOpen = false; // cancelled before anything was shown
            }

            return;
        }

        await PreviewAsync(picked).ConfigureAwait(true);
    }

    private bool CanChooseAgain() => !IsBusy;

    private async Task PreviewAsync(string newFolder)
    {
        _work?.Cancel();
        _work?.Dispose();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        IsBusy = true;
        NewFolder = newFolder;
        Lines.Clear();
        Movable = 0;
        _plan = null;
        SetMessage("Looking for the photos…", warning: false);
        try
        {
            var plan = await relocation.PreviewAsync(OldFolder!, newFolder, token).ConfigureAwait(true);
            _plan = plan;
            ShowPlan(plan);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            SetMessage(null, warning: false);
        }
        catch (ArgumentException)
        {
            SetMessage("That is the same folder. Choose where the folder is now.", warning: true);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            LogPreviewFailed(ex, newFolder);
            SetMessage("That folder can't be read: " + ex.Message, warning: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowPlan(RelocationPlan plan)
    {
        var culture = CultureInfo.CurrentCulture;
        void Add(RelocationOutcome outcome, string label, string? hint, bool warning)
        {
            var n = plan.Count(outcome);
            if (n > 0)
            {
                Lines.Add(new LocateCountLine(string.Create(culture, $"{n:N0} {label}"), hint, warning));
            }
        }

        Add(RelocationOutcome.Found, "found", null, false);
        Add(RelocationOutcome.ModifiedTimeDiffers, "found with a different date", "Probably copied. They move too and are read again at the next import.", false);
        Add(RelocationOutcome.DifferentSize, "with the same name but a different size", "Probably different photos. Left where they were.", true);
        Add(RelocationOutcome.NotFound, "not in the new folder", "Left as they were.", true);
        Add(RelocationOutcome.AlreadyInLibrary, "already in the library at the new place", "You imported the new folder already. Left as they were.", true);
        if (plan.SkippedFolders.Count > 0)
        {
            Lines.Add(new LocateCountLine(string.Create(culture, $"{plan.SkippedFolders.Count:N0} subfolder(s) couldn't be read"), null, true));
        }

        Movable = plan.Movable;
        SetMessage(
            plan.Items.Count == 0 ? "The library has no photos in that folder."
            : plan.Movable == 0 ? "None of the photos are in the folder you chose. Choose another folder?"
            : null,
            warning: plan.Movable == 0);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_plan is not { } plan)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var moved = await relocation.ApplyAsync(plan, CancellationToken.None).ConfigureAwait(true);
            IsOpen = false;
            Relocated?.Invoke(this, moved);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogApplyFailed(ex, plan.OldFolder, plan.NewFolder);
            SetMessage("Nothing was changed: the library couldn't be updated. See the log for details.", warning: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanApply() => Movable > 0 && !IsBusy;

    [RelayCommand]
    private void Close()
    {
        _work?.Cancel();
        IsOpen = false;
    }

    public void Dispose()
    {
        _work?.Cancel();
        _work?.Dispose();
        _work = null;
    }

    private void SetMessage(string? text, bool warning)
    {
        Message = text;
        IsMessageWarning = warning;
    }

    /// <summary>Offline folders with none of their parents offline too (what to locate).</summary>
    internal static List<string> TopFolders(IEnumerable<string> folders)
    {
        var list = folders.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f.Length).ToList();
        var top = new List<string>();
        foreach (var folder in list)
        {
            if (!top.Any(t => FolderScope.IsSameOrUnder(folder, t)))
            {
                top.Add(folder);
            }
        }

        return top;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} library folder(s) offline: {Reasons}")]
    private partial void LogOffline(int count, string reasons);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Locate folder: {Folder} could not be read.")]
    private partial void LogPreviewFailed(Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Error, Message = "Locate folder: moving {OldFolder} to {NewFolder} failed; nothing was changed.")]
    private partial void LogApplyFailed(Exception exception, string oldFolder, string newFolder);
}
