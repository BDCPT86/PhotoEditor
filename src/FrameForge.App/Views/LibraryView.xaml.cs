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
        // falls to the window, so no shortcut works until a click. Give it back to whatever is showing.
        foreach (var panel in new FrameworkElement[] { ExportSide, PresetsSide, DetailsSide, HelpSide })
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
