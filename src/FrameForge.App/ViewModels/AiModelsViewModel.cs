using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Ai;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>The "AI models" part of Settings: one row per catalogued model.</summary>
public sealed class AiModelsViewModel
{
    public AiModelsViewModel(IModelStore store, IAiRuntime runtime, IFileOpenPicker picker, TimeProvider time, ILogger<AiModelItemViewModel> logger)
    {
        Models = new ObservableCollection<AiModelItemViewModel>(
            AiModelCatalog.All.Select(m => new AiModelItemViewModel(m, store, runtime, picker, time, logger)));
    }

    public ObservableCollection<AiModelItemViewModel> Models { get; }

    public void Refresh()
    {
        foreach (var model in Models)
        {
            model.Refresh();
        }
    }
}

/// <summary>
/// One model: status, provenance and licence, and install/remove. A download only starts after the user opens the
/// consent panel (which shows size, source and licence) and confirms it; nothing is ever downloaded automatically.
/// </summary>
public sealed partial class AiModelItemViewModel : ObservableObject
{
    private readonly IModelStore _store;
    private readonly IAiRuntime _runtime;
    private readonly IFileOpenPicker _picker;
    private readonly TimeProvider _time;
    private readonly ILogger<AiModelItemViewModel> _logger;

    internal AiModelItemViewModel(ModelDescriptor model, IModelStore store, IAiRuntime runtime, IFileOpenPicker picker, TimeProvider time, ILogger<AiModelItemViewModel> logger)
    {
        Model = model;
        _store = store;
        _runtime = runtime;
        _picker = picker;
        _time = time;
        _logger = logger;
        Refresh();
    }

    public ModelDescriptor Model { get; }

    public string SizeText => FormatSize(Model.SizeBytes);

    public string LicenceText => $"{Model.Licence} — {Model.Copyright}";

    public string ConsentText => string.Create(
        CultureInfo.CurrentCulture,
        $"FrameForge will download {SizeText} from {Model.DownloadUrl.Host} ({Model.DownloadUrl}). The file is checked against SHA-256 {Model.Sha256} before it is used. Licence: {Model.Licence} ({Model.Copyright}). The model runs only on this computer; no photos are uploaded.");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(StateText))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private ModelState _state;

    public bool IsInstalled => State == ModelState.Installed;

    public string StateText => State switch
    {
        ModelState.Installed => "✓ Installed",
        ModelState.Invalid => "⚠ File present but does not match — remove and reinstall",
        _ => "Not installed",
    };

    [ObservableProperty]
    private bool _isConsentOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(ImportCommand), nameof(ShowConsentCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _hasError;

    public void Refresh() => State = _store.GetStatus(Model).State;

    private bool CanChange => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void ShowConsent()
    {
        Message = null;
        IsConsentOpen = true;
    }

    [RelayCommand]
    private void DeclineConsent() => IsConsentOpen = false;

    /// <summary>Only reachable from the consent panel's confirm button.</summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        IsConsentOpen = false;
        var consent = new ModelDownloadConsent(Model.Id, _time.GetUtcNow().UtcDateTime);
        await InstallAsync(
            "Downloading…",
            () => _store.DownloadAsync(Model, consent, new Progress<double>(p => Progress = p), cancellationToken)).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task ImportAsync()
    {
        var file = _picker.PickOpenFile($"Choose the {Model.Name} file ({Model.FileName})", "ONNX model (*.onnx)|*.onnx|All files (*.*)|*.*");
        if (file is null)
        {
            return;
        }

        await InstallAsync("Checking file…", () => _store.ImportAsync(Model, file, CancellationToken.None)).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        try
        {
            _store.Remove(Model);
            _runtime.Reset();
            SetMessage("Removed. AI results already stored stay until the photos are analysed again.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRemoveFailed(ex, Model.Id);
            SetMessage("The model could not be removed: " + ex.Message, true);
        }

        Refresh();
    }

    private bool CanRemove => !IsBusy && State != ModelState.NotInstalled;

    private async Task InstallAsync(string busyText, Func<Task<string>> install)
    {
        IsBusy = true;
        Progress = 0;
        SetMessage(busyText, false);
        try
        {
            await install().ConfigureAwait(true);
            _runtime.Reset();
            SetMessage("Installed and verified. Turn the capability on below, save, then run Analyze in the Library.", false);
        }
        catch (OperationCanceledException)
        {
            SetMessage("Download cancelled. Nothing was installed.", false);
        }
        catch (ModelVerificationException ex)
        {
            LogInstallFailed(ex, Model.Id);
            SetMessage("Not installed: " + ex.Message, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            LogInstallFailed(ex, Model.Id);
            SetMessage("Not installed: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void SetMessage(string? text, bool isError)
    {
        Message = text;
        HasError = isError;
    }

    internal static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d / 1024d:0.0} MB")
        : string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024d:0} KB");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model {ModelId} was not installed.")]
    private partial void LogInstallFailed(Exception exception, string modelId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Model {ModelId} could not be removed.")]
    private partial void LogRemoveFailed(Exception exception, string modelId);
}
