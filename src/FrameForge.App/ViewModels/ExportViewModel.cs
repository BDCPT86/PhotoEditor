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

/// <summary>Non-modal export panel: choose scope and mode, then a destination; shows progress and a summary.</summary>
public sealed partial class ExportViewModel(
    IExportService exporter,
    IEditedExportService editedExporter,
    IFolderPicker folderPicker,
    IFileSavePicker savePicker,
    IExportProfileService profileStore,
    ILogger<ExportViewModel> logger) : ObservableObject
{
    private Task? _profilesLoading;
    private bool _applyingProfile;

    /// <summary>Built-in profiles first, then the user's own.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ExportProfile> Profiles { get; } = [.. ExportProfiles.BuiltIn];

    /// <summary>The profile last chosen or saved; choosing one applies its settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileIsChanged), nameof(CanDeleteProfile))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private ExportProfile? _selectedProfile;

    /// <summary>Name for "Save as profile".</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProfileCommand))]
    private string _newProfileName = string.Empty;

    /// <summary>The name field for "Save as profile" is showing.</summary>
    [ObservableProperty]
    private bool _isNamingProfile;

    [RelayCommand]
    private void BeginNamingProfile()
    {
        ProfileMessage = null;
        NewProfileName = SelectedProfile is { } p && !ExportProfiles.IsBuiltInName(p.Name) ? p.Name : string.Empty;
        IsNamingProfile = true;
    }

    [RelayCommand]
    private void CancelNamingProfile()
    {
        IsNamingProfile = false;
        NewProfileName = string.Empty;
        ProfileMessage = null;
    }

    /// <summary>Why a profile couldn't be saved or removed, or null.</summary>
    [ObservableProperty]
    private string? _profileMessage;

    /// <summary>The settings no longer match the selected profile.</summary>
    public bool ProfileIsChanged => SelectedProfile is { } p && (CurrentSettings(p.Name) is not { } current || !current.HasSameSettings(p));

    /// <summary>Only the user's own profiles can be removed.</summary>
    public bool CanDeleteProfile => SelectedProfile is { } p && !ExportProfiles.IsBuiltInName(p.Name);

    /// <summary>Completes when the stored profiles and last-used settings have been loaded (started by the first Open).</summary>
    internal Task ProfilesLoaded => _profilesLoading ?? Task.CompletedTask;

    /// <summary>JPEG quality for edited exports.</summary>
    [ObservableProperty]
    private int _quality = EditedExportOptions.Default.Quality;

    /// <summary>Optional longest side for edited exports; empty keeps full size.</summary>
    [ObservableProperty]
    private string _maxLongSideText = string.Empty;

    /// <summary>Which of the original's metadata edited JPEGs carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWriteRatingAndLabel))]
    private ExportMetadata _metadata = EditedExportOptions.Default.Metadata;

    /// <summary>Write the FrameForge rating and colour label into edited JPEGs.</summary>
    [ObservableProperty]
    private bool _writeRatingAndLabel = EditedExportOptions.Default.WriteRatingAndLabel;

    /// <summary>The rating switch only applies when some metadata is written.</summary>
    public bool CanWriteRatingAndLabel => Metadata != ExportMetadata.None;

    /// <summary>File-name pattern for copies and edited JPEGs, such as "{date}_{name}"; empty keeps the original names.</summary>
    [ObservableProperty]
    private string _nameTemplateText = string.Empty;

    /// <summary>The first photo's export name under the current pattern, or null when there is nothing to show.</summary>
    [ObservableProperty]
    private string? _namePreview;

    /// <summary>Why the pattern can't be used, or null.</summary>
    [ObservableProperty]
    private string? _nameError;

    private Func<ExportScope, IReadOnlyList<ExportItem>> _source = _ => [];
    private IReadOnlyList<ExportItem> _previewItems = [];
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

    /// <summary>Which files of each RAW + JPEG pair a copy includes.</summary>
    [ObservableProperty]
    private PairFiles _pairFiles = PairFiles.Both;

    /// <summary>The photos in scope include a RAW + JPEG pair, so the choice of files applies.</summary>
    [ObservableProperty]
    private bool _hasPairs;

    /// <summary>How many files the export writes, when that differs from the number of photos (pairs); otherwise 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private int _fileCount;

    /// <summary>Photos counted as the grid shows them, a pair once; "12 photo(s) · 24 files" when a pair gives two files.</summary>
    public string CountText
    {
        get
        {
            var photos = Scope == ExportScope.Keep ? KeepCount : ViewCount;
            return FileCount > 0 && FileCount != photos
                ? string.Create(CultureInfo.CurrentCulture, $"{photos:N0} photo(s)  ·  {FileCount:N0} files")
                : string.Create(CultureInfo.CurrentCulture, $"{photos:N0} photo(s)");
        }
    }

    /// <summary>The files this mode exports: a copy follows the pair choice, edited JPEGs one per photo, the manifest every file.</summary>
    private IReadOnlyList<ExportItem> FilesFor(IReadOnlyList<ExportItem> items) => Mode switch
    {
        ExportMode.CopyFiles => ExportFrames.Select(items, PairFiles),
        ExportMode.EditedJpegs => ExportFrames.Covers(items),
        _ => items,
    };

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
        _profilesLoading ??= LoadProfilesAsync();
    }

    /// <summary>Loads the user's profiles and restores the last export's settings.</summary>
    private async Task LoadProfilesAsync()
    {
        ExportProfileSet stored;
        try
        {
            stored = await profileStore.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProfilesLoadFailed(ex);
            SelectMatchingProfile();
            return;
        }

        ShowProfiles(stored);
        if (stored.LastUsed is { } last)
        {
            Apply(last);
        }

        SelectMatchingProfile();
    }

    private void ShowProfiles(ExportProfileSet stored)
    {
        _applyingProfile = true;
        try
        {
            Profiles.Clear();
            foreach (var profile in ExportProfiles.BuiltIn.Concat(stored.Profiles))
            {
                Profiles.Add(profile);
            }
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    /// <summary>Selects the first profile holding exactly the current settings, without changing them.</summary>
    private void SelectMatchingProfile(string? preferredName = null)
    {
        var current = CurrentSettings(string.Empty);
        var match = Profiles.FirstOrDefault(p => preferredName is not null && string.Equals(p.Name, preferredName, StringComparison.OrdinalIgnoreCase))
            ?? (current is null ? null : Profiles.FirstOrDefault(p => current.HasSameSettings(p)));
        _applyingProfile = true;
        try
        {
            SelectedProfile = match;
        }
        finally
        {
            _applyingProfile = false;
        }
    }

    partial void OnSelectedProfileChanged(ExportProfile? value)
    {
        ProfileMessage = null;
        if (!_applyingProfile && value is not null)
        {
            Apply(value);
        }
    }

    private void Apply(ExportProfile profile)
    {
        _applyingProfile = true;
        try
        {
            Mode = profile.Mode;
            NameTemplateText = profile.NamePattern;
            Quality = profile.Quality;
            MaxLongSideText = profile.MaxLongSide?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            Metadata = profile.Metadata;
            WriteRatingAndLabel = profile.WriteRatingAndLabel;
            PairFiles = profile.PairFiles;
        }
        finally
        {
            _applyingProfile = false;
        }

        OnPropertyChanged(nameof(ProfileIsChanged));
    }

    /// <summary>The panel's settings as a profile, or null while the longest side isn't a number.</summary>
    internal ExportProfile? CurrentSettings(string name)
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

        return new ExportProfile
        {
            Name = name,
            Mode = Mode,
            NamePattern = NameTemplateText,
            Quality = Quality,
            MaxLongSide = maxSide,
            Metadata = Metadata,
            WriteRatingAndLabel = WriteRatingAndLabel,
            PairFiles = PairFiles,
        };
    }

    private void SettingChanged()
    {
        if (!_applyingProfile)
        {
            OnPropertyChanged(nameof(ProfileIsChanged));
        }
    }

    partial void OnQualityChanged(int value) => SettingChanged();

    partial void OnMaxLongSideTextChanged(string value) => SettingChanged();

    partial void OnMetadataChanged(ExportMetadata value) => SettingChanged();

    partial void OnWriteRatingAndLabelChanged(bool value) => SettingChanged();

    partial void OnPairFilesChanged(PairFiles value)
    {
        RefreshCounts();
        SettingChanged();
    }

    private bool CanSaveProfile() => !string.IsNullOrWhiteSpace(NewProfileName);

    [RelayCommand(CanExecute = nameof(CanSaveProfile))]
    private async Task SaveProfileAsync()
    {
        var name = NewProfileName.Trim();
        if (CurrentSettings(name) is not { } profile)
        {
            ProfileMessage = "The longest side must be a whole number of pixels, or empty for full size.";
            return;
        }

        if (profile.Validate() is { Count: > 0 } errors)
        {
            ProfileMessage = errors[0];
            return;
        }

        try
        {
            var stored = await profileStore.SaveProfileAsync(profile).ConfigureAwait(true);
            ShowProfiles(stored);
            SelectMatchingProfile(name);
            NewProfileName = string.Empty;
            IsNamingProfile = false;
        }
        catch (ArgumentException ex)
        {
            ProfileMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProfileSaveFailed(ex);
            ProfileMessage = "The profile couldn't be saved (see the log).";
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile || ExportProfiles.IsBuiltInName(profile.Name))
        {
            return;
        }

        try
        {
            var stored = await profileStore.DeleteProfileAsync(profile.Name).ConfigureAwait(true);
            ShowProfiles(stored);
            SelectMatchingProfile();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProfileSaveFailed(ex);
            ProfileMessage = "The profile couldn't be removed (see the log).";
        }
    }

    /// <summary>Remembers the settings of an export that is starting; a failure is logged and doesn't stop the export.</summary>
    private async Task RememberSettingsAsync()
    {
        if (CurrentSettings("Last used") is not { } settings || settings.Validate().Count > 0)
        {
            return;
        }

        try
        {
            await profileStore.SaveLastUsedAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProfileSaveFailed(ex);
        }
    }

    public void RefreshCounts()
    {
        var keep = _source(ExportScope.Keep);
        var view = _source(ExportScope.CurrentView);
        KeepCount = ExportFrames.Count(keep);
        ViewCount = ExportFrames.Count(view);
        var shown = Scope == ExportScope.Keep ? keep : view;
        HasPairs = ExportFrames.HasPairs(shown);
        var files = FilesFor(shown);
        FileCount = files.Count;
        OnPropertyChanged(nameof(CountText));
        _previewItems = files;
        UpdateNamePreview();
    }

    partial void OnScopeChanged(ExportScope value) => RefreshCounts();

    partial void OnModeChanged(ExportMode value)
    {
        RefreshCounts();
        SettingChanged();
    }

    partial void OnNameTemplateTextChanged(string value)
    {
        UpdateNamePreview();
        SettingChanged();
    }

    private void UpdateNamePreview()
    {
        if (!ExportNameTemplate.TryParse(NameTemplateText, out var template, out var errors))
        {
            NameError = errors[0];
            NamePreview = null;
            return;
        }

        NameError = null;

        // The first photo's names: both files of a pair ("IMG_0603.JPG, IMG_0603.CR3"), since they are named together.
        var first = _previewItems.Count == 0 ? []
            : _previewItems[0].Cover is { } cover ? _previewItems.TakeWhile(i => i.Cover?.Id == cover.Id).ToList()
            : [_previewItems[0]];
        var names = ExportFrames.WantedNames(first, template, Mode == ExportMode.EditedJpegs ? ".jpg" : null).Select(n => n.Name).ToList();
        NamePreview = names.Count switch
        {
            0 => null,
            1 => names[0],

            // A pair shares its name, so show it once: "2026-11-14_001.JPG + .CR3" fits the panel where two full names don't.
            _ when names.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.Ordinal).Count() == 1 =>
                names[0] + string.Concat(names.Skip(1).Select(n => " + " + Path.GetExtension(n))),
            _ => string.Join(", ", names),
        };
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
        var items = FilesFor(_source(Scope));
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

        if (!ExportNameTemplate.TryParse(NameTemplateText, out _, out var nameErrors))
        {
            SetResult($"The file-name pattern can't be used: {nameErrors[0]}", warning: true);
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
        await RunAsync(FilesFor(_source(Scope)), folder, manifest: false).ConfigureAwait(true);
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
        await RememberSettingsAsync().ConfigureAwait(true);

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
                var edited = items.Select(i => new EditedExportItem(i.Photo, i.Recipe ?? Core.Editing.EditRecipe.Neutral, i.Decision)).ToList();
                summary = await editedExporter.ExportAsync(edited, destination, options, progress, cts.Token).ConfigureAwait(true);
            }
            else
            {
                summary = await exporter.CopyAsync(items, destination, Naming(), progress, cts.Token).ConfigureAwait(true);
            }

            SetResult(Describe(summary, manifest, Mode == ExportMode.EditedJpegs, ExportFrames.Count(items)), warning: summary.Failures.Count > 0 || summary.Notes.Count > 0 || summary.WasCancelled);
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
            if (!manifest && Mode == ExportMode.EditedJpegs)
            {
                MemoryRelief.AfterLargeWork(); // every photo went through full-size buffers
            }
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
            Metadata = Metadata,
            WriteRatingAndLabel = WriteRatingAndLabel,
            Naming = Naming(),
        };
        return options.Validate().Count == 0 ? options : null;
    }

    /// <summary>The parsed pattern; checked in <see cref="StartAsync"/> before any export runs.</summary>
    private ExportNameTemplate Naming() =>
        ExportNameTemplate.TryParse(NameTemplateText, out var template, out _) ? template : ExportNameTemplate.Original;

    /// <param name="photos">Photos the files belong to, a RAW + JPEG pair once; shown when it differs from the number of files.</param>
    internal static string Describe(ExportSummary summary, bool manifest, bool edited = false, int? photos = null)
    {
        var culture = CultureInfo.CurrentCulture;
        var files = photos is { } p && p != summary.Requested
            ? string.Create(culture, $"{summary.Exported:N0} files ({p:N0} photos)")
            : string.Create(culture, $"{summary.Exported:N0} photo(s)");
        if (manifest)
        {
            return string.Create(culture, $"Manifest of {files} saved to {summary.Destination}.");
        }

        var verb = edited ? "saved as edited JPEGs" : "copied";
        var text = summary.WasCancelled
            ? string.Create(culture, $"Export cancelled: {summary.Exported:N0} of {summary.Requested:N0} {verb} to {summary.Destination}.")
            : edited
                ? string.Create(culture, $"Saved {summary.Exported:N0} edited JPEG(s) to {summary.Destination}.")
                : string.Create(culture, $"Copied {files} to {summary.Destination}.");

        if (summary.Renamed > 0)
        {
            text += string.Create(culture, $" {summary.Renamed:N0} renamed to avoid replacing existing files.");
        }

        if (summary.Notes.Count > 0)
        {
            var withoutMask = summary.Notes.Select(n => n.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            text += string.Create(culture, $" {withoutMask:N0} saved without a mask: ")
                + string.Join("; ", summary.Notes.Take(3).Select(n => $"{Path.GetFileName(n.SourcePath)} ({n.Note})"))
                + (summary.Notes.Count > 3 ? "…" : string.Empty);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading export profiles failed; showing the built-in ones.")]
    private partial void LogProfilesLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving export profiles failed.")]
    private partial void LogProfileSaveFailed(Exception exception);
}
