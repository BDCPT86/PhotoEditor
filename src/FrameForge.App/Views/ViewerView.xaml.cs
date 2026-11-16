using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FrameForge.App.Views;

public partial class ViewerView : UserControl
{
    public ViewerView()
    {
        InitializeComponent();

        // Focus management only: give the image keyboard focus when the viewer opens,
        // so arrow/zoom keys work immediately.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() => Zoom.Focus(), DispatcherPriority.Loaded);
            }
        };
    }
}
