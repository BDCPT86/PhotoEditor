using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FrameForge.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();

        // Focus management only: return keyboard focus to the selected tile when the viewer or compare
        // closes. Without this, focus leaves the page and page-level shortcuts stop reaching it.
        ViewerHost.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                FocusSelectedTile();
            }
        };
        CompareHost.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                FocusSelectedTile();
            }
        };

        // When a side panel closes (often by clicking its Close button), the focused control disappears and focus
        // falls to the window, so no shortcut works until a click. The offline banner does the same when "Check again"
        // finds the drive. Give focus back to whatever is showing.
        foreach (var panel in new FrameworkElement[] { ExportSide, PresetsSide, DetailsSide, HelpSide, LocateSide, OfflineBanner })
        {
            panel.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is false)
                {
                    Dispatcher.BeginInvoke(RestoreFocusAfterPanel, System.Windows.Threading.DispatcherPriority.Input);
                }
            };
        }
    }

    /// <summary>The chevron beside Import opens its options menu under it.</summary>
    private void OnImportOptionsClick(object sender, RoutedEventArgs e)
    {
        if (ImportOptions.ContextMenu is { } menu)
        {
            menu.PlacementTarget = ImportOptions;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    // ---------------------------------------------------------------- drag and drop: a folder from Explorer imports it

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.FirstOrDefault(System.IO.Directory.Exists)
            : null;

    private bool CanAcceptDrop(DragEventArgs e) =>
        DataContext is ViewModels.LibraryViewModel library
        && !library.IsViewerOpen && !library.IsCompareOpen
        && DroppedFolder(e) is { } folder && library.ImportDroppedCommand.CanExecute(folder);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var accept = CanAcceptDrop(e);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        if (accept && DroppedFolder(e) is { } folder)
        {
            DropHintText.Text = $"Drop to import “{System.IO.Path.GetFileName(folder.TrimEnd('\\'))}”";
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (CanAcceptDrop(e) && DroppedFolder(e) is { } folder && DataContext is ViewModels.LibraryViewModel library)
        {
            library.ImportDroppedCommand.Execute(folder);
        }

        e.Handled = true;
    }

    private void RestoreFocusAfterPanel()
    {
        if (!IsVisible || (IsKeyboardFocusWithin && Keyboard.FocusedElement is UIElement { IsVisible: true }))
        {
            return;
        }

        if (ViewerHost.IsVisible)
        {
            ViewerHost.FocusPhoto();
        }
        else if (CompareHost.IsVisible)
        {
            CompareHost.Focus();
        }
        else
        {
            FocusSelectedTile();
        }
    }

    private void FocusSelectedTile()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (PhotoGrid.SelectedItem is { } item)
            {
                PhotoGrid.ScrollIntoView(item);
                if (PhotoGrid.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
                {
                    container.Focus();
                    return;
                }
            }

            PhotoGrid.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
