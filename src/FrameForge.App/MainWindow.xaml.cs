using System.Windows;
using FrameForge.App.ViewModels;

namespace FrameForge.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
