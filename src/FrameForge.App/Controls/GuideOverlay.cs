using System.Windows;
using System.Windows.Media;
using FrameForge.Application.Editing.Auto;

namespace FrameForge.App.Controls;

/// <summary>
/// Draws a guide line (normalised to the image) over an image shown fitted to the same area, e.g. the line Auto level
/// will make horizontal. Not hit-testable, so it never gets in the way of the photo underneath.
/// </summary>
public sealed class GuideOverlay : FrameworkElement
{
    public static readonly DependencyProperty GuideProperty = DependencyProperty.Register(
        nameof(Guide), typeof(GuideLine), typeof(GuideOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(System.Windows.Media.Imaging.BitmapSource), typeof(GuideOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen Dark = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 3));
    private static readonly Pen Light = Freeze(new Pen(Brushes.White, 1.5) { DashStyle = new DashStyle([6, 4], 0) });

    public GuideOverlay() => IsHitTestVisible = false;

    public GuideLine? Guide
    {
        get => (GuideLine?)GetValue(GuideProperty);
        set => SetValue(GuideProperty, value);
    }

    /// <summary>The displayed image, whose pixel size tells where it sits when fitted.</summary>
    public System.Windows.Media.Imaging.BitmapSource? Image
    {
        get => (System.Windows.Media.Imaging.BitmapSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Guide is not { } g || Image is not { PixelWidth: > 0 } image || ActualWidth <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth / image.PixelWidth, ActualHeight / image.PixelHeight);
        double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
        double left = (ActualWidth - w) / 2, top = (ActualHeight - h) / 2;
        var a = new Point(left + (g.X1 * w), top + (g.Y1 * h));
        var b = new Point(left + (g.X2 * w), top + (g.Y2 * h));
        drawingContext.PushClip(new RectangleGeometry(new Rect(left, top, w, h)));
        drawingContext.DrawLine(Dark, a, b); // a dark underlay keeps the dashed line visible on white kit and sky
        drawingContext.DrawLine(Light, a, b);
        drawingContext.Pop();
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
