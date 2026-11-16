using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Settings;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ShellViewModel> _logger;

    public ShellViewModel(
        LibraryViewModel library,
        SettingsViewModel settingsPage,
        AboutViewModel about,
        ISettingsService settings,
        ILogger<ShellViewModel> logger)
    {
        _settings = settings;
        _logger = logger;

        Pages =
        [
            new NavigationItem(AppPage.Library, "Ctrl+1", library),
            new NavigationItem(AppPage.Settings, "Ctrl+2", settingsPage),
            new NavigationItem(AppPage.About, "Ctrl+3", about),
        ];
    }

    public IReadOnlyList<NavigationItem> Pages { get; }

    [ObservableProperty]
    private NavigationItem? _selectedPage;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    private bool _isReady;

    [ObservableProperty]
    private string _statusMessage = "Starting…";

    /// <summary>Opens the user's preferred start page and enables navigation.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var startPage = await _settings.GetAsync(AppSettings.StartPage, cancellationToken).ConfigureAwait(true);
        IsReady = true;
        Navigate(startPage);
        StatusMessage = "Ready";
    }

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void Navigate(AppPage page) =>
        SelectedPage = Pages.FirstOrDefault(p => p.Page == page) ?? Pages[0];

    partial void OnSelectedPageChanged(NavigationItem? value)
    {
        CurrentPage = value?.ViewModel;
        if (CurrentPage is not null)
        {
            _ = ActivatePageAsync(CurrentPage);
        }
    }

    private async Task ActivatePageAsync(PageViewModel page)
    {
        try
        {
            await page.OnNavigatedToAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogActivationFailed(ex, page.Title);
            StatusMessage = $"The {page.Title} page could not be loaded. See the log for details.";
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Activating page {PageTitle} failed.")]
    private partial void LogActivationFailed(Exception exception, string pageTitle);
}
