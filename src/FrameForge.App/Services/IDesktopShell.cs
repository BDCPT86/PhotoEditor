using System.Diagnostics;
using System.Windows;

namespace FrameForge.App.Services;

/// <summary>Small hand-offs to Windows: show a file in File Explorer, put text on the clipboard.</summary>
public interface IDesktopShell
{
    void ShowInFolder(string path);

    void CopyToClipboard(string text);
}

public sealed class WindowsDesktopShell : IDesktopShell
{
    public void ShowInFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public void CopyToClipboard(string text) => Clipboard.SetText(text);
}
