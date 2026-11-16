using System.IO;
using Microsoft.Win32;

namespace FrameForge.App.Services;

public interface IFolderPicker
{
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    string? PickFolder(string title, string? initialFolder);
}

public interface IFileSavePicker
{
    /// <returns>The chosen file path (overwrite already confirmed by the dialog), or null if cancelled.</returns>
    string? PickSaveFile(string title, string defaultFileName, string filter);
}

internal sealed class WindowsFileSavePicker : IFileSavePicker
{
    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            OverwritePrompt = true,
            AddExtension = true,
        };

        return dialog.ShowDialog(System.Windows.Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }
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
