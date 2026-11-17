using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.Application.Editing.Auto;

namespace FrameForge.App.ViewModels;

/// <summary>One Auto option in the suggestions tray: its full preview, a thumbnail of the same render, and why.</summary>
public sealed partial class SuggestionItem(AutoSuggestion suggestion, int number, BitmapSource preview) : ObservableObject
{
    public AutoSuggestion Suggestion { get; } = suggestion;

    /// <summary>1-based; the number key that picks it.</summary>
    public int Number { get; } = number;

    public string Name => Suggestion.Name;

    public string Reason => Suggestion.Reason;

    /// <summary>The option rendered at preview size; also shown (scaled) as the thumbnail.</summary>
    public BitmapSource Preview { get; } = preview;

    public GuideLine? Guide => Suggestion.Guide;

    public string AccessibleName => $"Option {Number}: {Name}. {Reason}";

    [ObservableProperty]
    private bool _isFocused;
}
