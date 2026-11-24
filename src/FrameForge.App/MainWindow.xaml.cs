using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameForge.App.ViewModels;
using FrameForge.Core.Settings;

namespace FrameForge.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    /// <summary>
    /// A library view or folder chosen in the sidebar by the user (keyboard, including screen readers, or mouse) also
    /// brings the library page forward. Selections made by code (start-up, the folder list refreshing after an import)
    /// happen without keyboard focus in the list and are ignored.
    /// </summary>
    private void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { IsKeyboardFocusWithin: true } && e.AddedItems.Count > 0)
        {
            ShowLibrary();
        }
    }

    /// <summary>Clicking the view that is already selected (while on Settings) still returns to the library.</summary>
    private void OnSidebarClicked(object sender, MouseButtonEventArgs e) => ShowLibrary();

    private void ShowLibrary()
    {
        if (!_viewModel.IsLibraryPage && _viewModel.NavigateCommand.CanExecute(AppPage.Library))
        {
            _viewModel.NavigateCommand.Execute(AppPage.Library);
        }
    }
}
