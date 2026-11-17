using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>Side panel: apply a saved preset to the selected photos in one batch (progress, cancel, undo).</summary>
public sealed partial class PresetPanelViewModel(EditController edits, ILogger<PresetPanelViewModel> logger) : ObservableObject
{
    private Func<IReadOnlyList<PhotoTileViewModel>> _targets = () => [];
    private CancellationTokenSource? _cts;

    public EditController Edits { get; } = edits;

    public ObservableCollection<EditPreset> Presets => Edits.Presets;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private EditPreset? _selectedPreset;

    [ObservableProperty]
    private int _targetCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(UndoBatchCommand), nameof(ApplyAutoCommand))]
    private bool _isApplying;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private bool _resultIsWarning;

    public void Open(Func<IReadOnlyList<PhotoTileViewModel>> targets)
    {
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        TargetCount = _targets().Count;
        ResultText = null;
        IsOpen = true;
    }

    public void RefreshTargets() => TargetCount = _targets().Count;

    [RelayCommand]
    private void Close()
    {
        if (!IsApplying)
        {
            IsOpen = false;
        }
    }

    private bool CanApply() => !IsApplying && SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var targets = _targets();
        if (SelectedPreset is not { } preset || targets.Count == 0)
        {
            SetResult("Select photos in the grid first (Ctrl+click or Shift+arrow keys).", warning: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsApplying = true;
        ProgressValue = 0;
        ResultText = null;
        try
        {
            var result = await Edits.ApplyPresetAsync(
                targets,
                preset,
                new Progress<BatchProgress>(p => ProgressValue = p.Total == 0 ? 1 : (double)p.Completed / p.Total),
                cts.Token).ConfigureAwait(true);
            var culture = CultureInfo.CurrentCulture;
            SetResult(
                result.WasCancelled
                    ? string.Create(culture, $"Cancelled: “{preset.Name}” was applied to {result.Changes.Count:N0} of {targets.Count:N0} photos. Undo restores them.")
                    : string.Create(culture, $"Applied “{preset.Name}” to {result.Changes.Count:N0} photo(s)."),
                warning: result.WasCancelled);
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or InvalidOperationException or ArgumentException)
        {
            LogBatchFailed(ex, preset.Name);
            SetResult("The preset could not be applied. Photos saved before the error keep it; see the log.", warning: true);
        }
        finally
        {
            _cts = null;
            IsApplying = false;
        }
    }

    // ---------------------------------------------------------------- Auto on many photos

    public IReadOnlyList<string> LightVariants { get; } = ["Natural", "Brighter", "Punchy"];

    public IReadOnlyList<string> CropVariants { get; } = ["Tight", "Medium", "Wide"];

    public IReadOnlyList<string> CropAspects { get; } = ["Original", "1:1", "4:5", "3:2", "16:9"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAutoCommand))]
    private bool _autoLight;

    [ObservableProperty]
    private string _lightVariant = "Natural";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAutoCommand))]
    private bool _autoLevel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAutoCommand))]
    private bool _autoCrop;

    [ObservableProperty]
    private string _cropVariant = "Medium";

    [ObservableProperty]
    private string _cropAspect = "Original";

    /// <summary>Photos the last Auto run could not fully handle, and why (e.g. no faces to frame).</summary>
    public ObservableCollection<string> AutoNotes { get; } = [];

    internal AutoBatchOptions AutoOptions => new()
    {
        Light = AutoLight ? LightVariant : null,
        Level = AutoLevel,
        Crop = AutoCrop ? CropVariant : null,
        CropAspect = CropAspect,
    };

    private bool CanApplyAuto() => !IsApplying && AutoOptions.Any;

    [RelayCommand(CanExecute = nameof(CanApplyAuto))]
    private async Task ApplyAutoAsync()
    {
        var targets = _targets();
        if (targets.Count == 0)
        {
            SetResult("Select photos in the grid first (Ctrl+click or Shift+arrow keys).", warning: true);
            return;
        }

        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsApplying = true;
        ProgressValue = 0;
        ResultText = null;
        AutoNotes.Clear();
        try
        {
            var result = await Edits.ApplyAutoAsync(
                targets,
                AutoOptions,
                new Progress<BatchProgress>(p => ProgressValue = p.Total == 0 ? 1 : (double)p.Completed / p.Total),
                cts.Token).ConfigureAwait(true);

            var names = targets.ToDictionary(t => t.Id, t => t.FileName);
            foreach (var (id, note) in result.Notes)
            {
                AutoNotes.Add($"{names.GetValueOrDefault(id, "?")}: {note}");
            }

            var culture = CultureInfo.CurrentCulture;
            var head = result.WasCancelled
                ? string.Create(culture, $"Cancelled after changing {result.Changes.Count:N0} photo(s); Undo restores them.")
                : string.Create(culture, $"Auto changed {result.Changes.Count:N0} of {targets.Count:N0} photo(s).");
            var tail = result.Notes.Count == 0 ? string.Empty
                : string.Create(culture, $" {result.Notes.Count:N0} could not be fully handled (listed above).");
            SetResult(head + tail + " Use the “Auto-applied” filter to review them.", warning: result.WasCancelled || result.Notes.Count > 0);
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or InvalidOperationException or ArgumentException or Application.Photos.PhotoReadException)
        {
            LogBatchFailed(ex, "Auto");
            SetResult("Auto stopped because of an error. Photos saved before it keep their changes; see the log.", warning: true);
        }
        finally
        {
            _cts = null;
            IsApplying = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanUndoBatch() => !IsApplying;

    [RelayCommand(CanExecute = nameof(CanUndoBatch))]
    private async Task UndoBatchAsync()
    {
        var restored = await Edits.UndoLastBatchAsync().ConfigureAwait(true);
        SetResult(
            restored == 0 ? "Nothing to undo." : string.Create(CultureInfo.CurrentCulture, $"Restored the previous edits of {restored:N0} photo(s)."),
            warning: false);
    }

    private void SetResult(string text, bool warning)
    {
        ResultText = text;
        ResultIsWarning = warning;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Applying preset {Preset} failed.")]
    private partial void LogBatchFailed(Exception exception, string preset);
}
