using FrameForge.Core.Settings;

namespace FrameForge.App.ViewModels;

public sealed record NavigationItem(AppPage Page, string ShortcutText, PageViewModel ViewModel)
{
    public string Title => ViewModel.Title;
}
