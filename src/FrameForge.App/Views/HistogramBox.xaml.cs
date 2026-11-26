using System.Windows;
using System.Windows.Controls;

namespace FrameForge.App.Views;

/// <summary>The histogram with its clipped shares (M09). The data context is a <see cref="ViewModels.HistogramViewModel"/>.</summary>
public partial class HistogramBox : UserControl
{
    public static readonly DependencyProperty CurveHeightProperty = DependencyProperty.Register(
        nameof(CurveHeight), typeof(double), typeof(HistogramBox), new PropertyMetadata(72.0));

    public HistogramBox() => InitializeComponent();

    /// <summary>Height of the curve area, in DIPs.</summary>
    public double CurveHeight
    {
        get => (double)GetValue(CurveHeightProperty);
        set => SetValue(CurveHeightProperty, value);
    }
}
