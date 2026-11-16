using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FrameForge.Core.Decisions;

namespace FrameForge.App.Converters;

/// <summary>Maps a colour label to its theme brush (Brush.Label.Red etc.); None maps to transparent.</summary>
public sealed class ColourLabelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ColourLabel label && label != ColourLabel.None
            && System.Windows.Application.Current?.TryFindResource($"Brush.Label.{label}") is Brush brush
            ? brush
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
