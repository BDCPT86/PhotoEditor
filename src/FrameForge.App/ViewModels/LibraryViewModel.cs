namespace FrameForge.App.ViewModels;

/// <summary>Placeholder for the photo library. Import and browsing are not part of this milestone.</summary>
public sealed class LibraryViewModel : PageViewModel
{
    public override string Title => "Library";

    public string EmptyStateHeading { get; } = "Your library is empty";

    public string EmptyStateMessage { get; } = "Photo import is not available yet.";
}
