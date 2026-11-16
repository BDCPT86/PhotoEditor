using CommunityToolkit.Mvvm.ComponentModel;

namespace FrameForge.App.ViewModels;

/// <summary>Base for top-level pages hosted by the shell.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Called each time the page becomes the current page. Implementations handle their own errors.</summary>
    public virtual Task OnNavigatedToAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
