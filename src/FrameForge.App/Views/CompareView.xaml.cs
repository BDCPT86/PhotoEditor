using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FrameForge.App.Views;

public partial class CompareView : UserControl
{
    public CompareView()
    {
        InitializeComponent();

        // Focus management only: take keyboard focus when shown so ← → and decision keys work at once.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Loaded);
            }
        };
    }
}
