using System.Windows;

namespace FrameForge.App.Controls;

/// <summary>
/// Pure zoom/pan calculations. "Zoom" is device pixels per image pixel, so 1.0 is 100%
/// (one image pixel per screen pixel) regardless of Windows display scaling.
/// </summary>
internal static class ZoomMath
{
    public const double MaxZoom = 8.0;

    /// <summary>Zoom that fits the whole image in the viewport, never enlarging beyond 100%.</summary>
    public static double FitZoom(double imagePixelWidth, double imagePixelHeight, double viewportWidth, double viewportHeight, double dpiScale)
    {
        if (imagePixelWidth <= 0 || imagePixelHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0 || dpiScale <= 0)
        {
            return 1.0;
        }

        var fit = Math.Min(viewportWidth * dpiScale / imagePixelWidth, viewportHeight * dpiScale / imagePixelHeight);
        return Math.Min(fit, 1.0);
    }

    /// <summary>Clamps between the fit zoom (cannot zoom out past fit) and <see cref="MaxZoom"/>.</summary>
    public static double ClampZoom(double zoom, double fitZoom) =>
        Math.Clamp(zoom, Math.Min(fitZoom, 1.0), MaxZoom);

    /// <summary>
    /// New top-left offset (DIPs) that keeps the image point under <paramref name="anchor"/>
    /// fixed while scale changes from <paramref name="oldScale"/> to <paramref name="newScale"/>.
    /// </summary>
    public static Point ZoomAt(Point offset, Point anchor, double oldScale, double newScale)
    {
        var ratio = newScale / oldScale;
        return new Point(
            anchor.X - ((anchor.X - offset.X) * ratio),
            anchor.Y - ((anchor.Y - offset.Y) * ratio));
    }

    /// <summary>
    /// Keeps content covering the viewport when it is larger, and centred when it is smaller,
    /// so the image can never be dragged out of sight.
    /// </summary>
    public static Point ClampOffset(Point offset, Size content, Size viewport) =>
        new(ClampAxis(offset.X, content.Width, viewport.Width), ClampAxis(offset.Y, content.Height, viewport.Height));

    private static double ClampAxis(double offset, double content, double viewport) =>
        content <= viewport
            ? (viewport - content) / 2
            : Math.Clamp(offset, viewport - content, 0);
}
