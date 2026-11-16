using FrameForge.App.Startup;

namespace FrameForge.App.ViewModels;

public sealed class AboutViewModel(AppInfo appInfo) : PageViewModel
{
    public override string Title => "About";

    public AppInfo Info { get; } = appInfo;
}
