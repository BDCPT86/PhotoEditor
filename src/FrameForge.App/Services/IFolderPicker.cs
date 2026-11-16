using System.IO;
using Microsoft.Win32;

namespace FrameForge.App.Services;

public interface IFolderPicker
{
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    string? PickFolder(string title, string? initialFolder);
}

internal sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }
}
