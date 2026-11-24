using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FrameForge.App.Converters;

/// <summary>Folder depth → left margin for the sidebar's folder tree (14 px per level).</summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    public const double Step = 14;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
