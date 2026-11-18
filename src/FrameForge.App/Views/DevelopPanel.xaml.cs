using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameForge.App.ViewModels;

namespace FrameForge.App.Views;

public partial class DevelopPanel
{
    public DevelopPanel() => InitializeComponent();

    /// <summary>Double-clicking a slider resets that setting (the slider's Tag names it), globally or for one mask.</summary>
    private void OnSliderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider { Tag: string name } slider)
        {
            return;
        }

        if (slider.DataContext is LocalAdjustmentViewModel local)
        {
            local.ResetSettingCommand.Execute(name);
            e.Handled = true;
        }
        else if (DataContext is DevelopViewModel develop)
        {
            develop.ResetSettingCommand.Execute(name);
            e.Handled = true;
        }
    }
}
