using System.Windows;
using System.Windows.Controls;

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
