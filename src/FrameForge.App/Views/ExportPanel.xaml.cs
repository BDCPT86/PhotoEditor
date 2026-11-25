using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FrameForge.App.Views;

public partial class ExportPanel : UserControl
{
    public ExportPanel() => InitializeComponent();

    /// <summary>"Save as profile…" opens the name field: put the cursor in it, with any suggested name selected.</summary>
    private void ProfileNameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
        {
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }
}
