using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FrameForge.App.Views;

public partial class RecycleRejectsPanel : UserControl
{
    public RecycleRejectsPanel()
    {
        InitializeComponent();

        // Opened from the grid (Delete or the heading button): take keyboard focus so Esc closes the panel. Focus goes
        // to Cancel, never to the move button, so a stray Enter can't move anything.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => CancelButton.Focus());
            }
        };
    }
}
