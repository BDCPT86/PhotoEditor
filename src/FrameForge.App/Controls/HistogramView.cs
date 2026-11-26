using System.Windows;
using System.Windows.Media;
using FrameForge.Analysis;

namespace FrameForge.App.Controls;

/// <summary>
/// Draws an <see cref="ImageHistogram"/>: luminance as a soft filled shape, red, green and blue as thin lines over it
/// (M09 C1). Drawn in one pass of frozen geometry, so a new histogram costs one render, not hundreds of elements.
/// </summary>
/// <remarks>
/// The height scale is the tallest bin between levels 1 and 254 of any channel: a spike of pure black or white (a
/// clipped sky) runs off the top instead of flattening everything else. The clipped shares are written under the
/// histogram, so the spike's size is never hidden.
/// </remarks>
public sealed class HistogramView : FrameworkElement
{
    public static readonly DependencyProperty HistogramProperty = DependencyProperty.Register(
        nameof(Histogram), typeof(ImageHistogram), typeof(HistogramView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LuminanceBrushProperty = Brush(nameof(LuminanceBrush), Color.FromRgb(0x5A, 0x5A, 0x5A));
    public static readonly DependencyProperty RedBrushProperty = Brush(nameof(RedBrush), Color.FromRgb(0xE0, 0x6C, 0x5C));
    public static readonly DependencyProperty GreenBrushProperty = Brush(nameof(GreenBrush), Color.FromRgb(0x6F, 0xBF, 0x73));
    public static readonly DependencyProperty BlueBrushProperty = Brush(nameof(BlueBrush), Color.FromRgb(0x5B, 0x8D, 0xEF));

    private static DependencyProperty Brush(string name, Color fallback)
    {
        var brush = new SolidColorBrush(fallback);
        brush.Freeze();
        return DependencyProperty.Register(name, typeof(Brush), typeof(HistogramView),
            new FrameworkPropertyMetadata(brush, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public ImageHistogram? Histogram
    {
        get => (ImageHistogram?)GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public Brush LuminanceBrush
    {
        get => (Brush)GetValue(LuminanceBrushProperty);
        set => SetValue(LuminanceBrushProperty, value);
    }

    public Brush RedBrush
    {
        get => (Brush)GetValue(RedBrushProperty);
        set => SetValue(RedBrushProperty, value);
    }

    public Brush GreenBrush
    {
        get => (Brush)GetValue(GreenBrushProperty);
        set => SetValue(GreenBrushProperty, value);
    }

    public Brush BlueBrush
    {
        get => (Brush)GetValue(BlueBrushProperty);
        set => SetValue(BlueBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Histogram is not { PixelCount: > 0 } histogram || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var luminance = Smooth(histogram.Luminance);
        var channels = new[] { (Smooth(histogram.Red), RedBrush), (Smooth(histogram.Green), GreenBrush), (Smooth(histogram.Blue), BlueBrush) };
        var scale = ScaleOf([luminance, .. channels.Select(c => c.Item1)]);
        if (scale <= 0)
        {
            return;
        }

        drawingContext.DrawGeometry(LuminanceBrush, null, Shape(luminance, scale, filled: true));
        foreach (var (bins, brush) in channels)
        {
            var pen = new Pen(brush, 1.25) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawingContext.PushOpacity(0.85);
            drawingContext.DrawGeometry(null, pen, Shape(bins, scale, filled: false));
            drawingContext.Pop();
        }
    }

    /// <summary>
    /// For drawing only: each bin averaged with its neighbours (1-2-1). An edit's gain leaves every other level empty in an
    /// 8-bit image, which would draw as a comb; the counts and clipped shares themselves are never smoothed.
    /// </summary>
    internal static double[] Smooth(IReadOnlyList<int> bins)
    {
        var smoothed = new double[bins.Count];
        for (var i = 0; i < bins.Count; i++)
        {
            var left = bins[Math.Max(0, i - 1)];
            var right = bins[Math.Min(bins.Count - 1, i + 1)];
            smoothed[i] = (left + (2.0 * bins[i]) + right) / 4;
        }

        return smoothed;
    }

    /// <summary>The tallest (smoothed) bin between levels 1 and 254 of any channel, or the tallest overall if those are empty.</summary>
    internal static double ScaleOf(IReadOnlyList<double[]> curves)
    {
        var max = 0.0;
        foreach (var bins in curves)
        {
            for (var i = 1; i < ImageHistogram.Bins - 1; i++)
            {
                max = Math.Max(max, bins[i]);
            }
        }

        return max > 0 ? max : curves.Max(b => b.Max());
    }

    private StreamGeometry Shape(double[] bins, double scale, bool filled)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var step = width / (ImageHistogram.Bins - 1);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Point At(int i) => new(i * step, height - (Math.Min(bins[i], scale) * (height - 1) / scale));
            context.BeginFigure(filled ? new Point(0, height) : At(0), isFilled: filled, isClosed: filled);
            if (filled)
            {
                context.LineTo(At(0), isStroked: false, isSmoothJoin: false);
            }

            for (var i = 1; i < ImageHistogram.Bins; i++)
            {
                context.LineTo(At(i), isStroked: true, isSmoothJoin: true);
            }

            if (filled)
            {
                context.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
