using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.App.Services;
using FrameForge.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// After start-up: if FrameForge wrote crash reports since the user last looked, a quiet notice offers to show or copy
/// the newest one. Reports stay on the computer; sharing one is up to the user (ADR 0019).
/// </summary>
public sealed partial class CrashNoticeViewModel(ICrashReports reports, IDesktopShell desktop, ILogger<CrashNoticeViewModel> logger) : ObservableObject
{
    private string? _newest;

    /// <summary>The notice text, or null when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private string? _message;

    public bool IsShown => Message is not null;

    public void Check()
    {
        IReadOnlyList<string> unseen;
        try
        {
            unseen = reports.Unseen();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCheckFailed(ex);
            return;
        }

        if (unseen.Count == 0)
        {
            Message = null;
            return;
        }

        _newest = unseen[0];
        Message = unseen.Count == 1
            ? "FrameForge had a problem last time and saved a report on this computer. Nothing was sent."
            : string.Create(CultureInfo.CurrentCulture, $"FrameForge had {unseen.Count} problems since you last looked and saved reports on this computer. Nothing was sent.");
    }

    [RelayCommand]
    private void ShowReport()
    {
        if (_newest is { } path)
        {
            desktop.ShowInFolder(path);
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (_newest is { } path && reports.Read(path) is { } text)
        {
            desktop.CopyToClipboard(text);
            Message = "Report copied. Paste it wherever you want to share it, or dismiss this note.";
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        reports.MarkSeen();
        Message = null;
        _newest = null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Looking for crash reports failed.")]
    private partial void LogCheckFailed(Exception exception);
}
