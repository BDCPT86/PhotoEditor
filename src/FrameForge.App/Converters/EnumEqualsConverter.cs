using System.Globalization;
using System.Windows.Data;

namespace FrameForge.App.Converters;

/// <summary>
/// Binds a group of RadioButtons to one enum property: checked when the bound value equals
/// the ConverterParameter; checking a button writes its parameter back.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
