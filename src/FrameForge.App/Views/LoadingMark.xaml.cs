using System.Windows;
using System.Windows.Controls;

namespace FrameForge.App.Views;

/// <summary>The loading state: the FrameForge mark with a status line.</summary>
public partial class LoadingMark : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LoadingMark), new FrameworkPropertyMetadata(string.Empty));

    public LoadingMark() => InitializeComponent();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
