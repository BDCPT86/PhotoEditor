using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settings, ILogger<SettingsViewModel> logger) : PageViewModel
{
    public override string Title => "Settings";

    public string LogRetentionHint { get; } =
        $"Whole days, {AppSettings.MinLogRetentionDays}–{AppSettings.MaxLogRetentionDays}. Older log files are removed when FrameForge starts.";

    [ObservableProperty]
    private AppPage _startPage;

    [ObservableProperty]
    private string _logRetentionDaysText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasError;

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        try
        {
            StartPage = await settings.GetAsync(AppSettings.StartPage, cancellationToken).ConfigureAwait(true);
            var days = await settings.GetAsync(AppSettings.LogRetentionDays, cancellationToken).ConfigureAwait(true);
            LogRetentionDaysText = days.ToString(CultureInfo.CurrentCulture);
            SetStatus(null, isError: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(ex);
            SetStatus("Settings could not be loaded. See the log for details.", isError: true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(LogRetentionDaysText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var days)
            || !AppSettings.LogRetentionDays.IsValid(days))
        {
            SetStatus($"Log retention must be a whole number from {AppSettings.MinLogRetentionDays} to {AppSettings.MaxLogRetentionDays}.", isError: true);
            return;
        }

        try
        {
            await settings.SetAsync(AppSettings.StartPage, StartPage, cancellationToken).ConfigureAwait(true);
            await settings.SetAsync(AppSettings.LogRetentionDays, days, cancellationToken).ConfigureAwait(true);
            SetStatus("Settings saved. They take effect the next time FrameForge starts.", isError: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(ex);
            SetStatus("Settings could not be saved. See the log for details.", isError: true);
        }
    }

    private void SetStatus(string? message, bool isError)
    {
        StatusMessage = message;
        HasError = isError;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading settings failed.")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving settings failed.")]
    private partial void LogSaveFailed(Exception exception);
}
