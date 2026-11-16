using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using FrameForge.App.ViewModels;
using FrameForge.Core.Editing;

namespace FrameForge.App.Controls;

/// <summary>
/// Shows an image fitted to the control with an adjustable crop rectangle (normalised 0–1). Drag inside to move,
/// drag a corner or edge to resize. Keyboard: arrows move by 1 % (Shift = 5 %), Ctrl+arrows resize from the
/// bottom-right. An optional locked ratio (normalised width÷height) is kept while resizing.
/// </summary>
public sealed class CropOverlay : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(CropOverlay), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CropProperty = DependencyProperty.Register(
        nameof(Crop), typeof(CropRect), typeof(CropOverlay),
        new FrameworkPropertyMetadata(CropRect.Full, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty LockedRatioProperty = DependencyProperty.Register(
        nameof(LockedRatio), typeof(double), typeof(CropOverlay), new FrameworkPropertyMetadata(0.0));

    private const double HandleSize = 14;
    private static readonly Brush Shade = Freeze(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)));
    private static readonly Pen Border = Freeze(new Pen(Brushes.White, 1.5));
    private static readonly Pen Thirds = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), 1));

    private DragMode _drag;
    private Point _dragStart;
    private CropRect _startCrop = CropRect.Full;

    public CropOverlay()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        AutomationProperties.SetName(this, "Crop area. Arrow keys move it; Ctrl+arrow keys resize it.");

        // Take keyboard focus when crop mode opens, so arrow keys adjust the crop straight away.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
        };
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new CropOverlayAutomationPeer(this);

    private sealed class CropOverlayAutomationPeer(CropOverlay owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            System.Windows.Automation.Peers.AutomationControlType.Custom;

        protected override string GetClassNameCore() => nameof(CropOverlay);

        protected override bool IsKeyboardFocusableCore() => true;

        protected override string GetItemStatusCore()
        {
            var crop = ((CropOverlay)Owner).Crop;
            return string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{crop.Width:P0} × {crop.Height:P0} at {crop.X:P0}, {crop.Y:P0}");
        }
    }

    [Flags]
    internal enum DragMode
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Move = 16,
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CropRect Crop
    {
        get => (CropRect)GetValue(CropProperty);
        set => SetValue(CropProperty, value);
    }

    public double LockedRatio
    {
        get => (double)GetValue(LockedRatioProperty);
        set => SetValue(LockedRatioProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // hit-testable everywhere
        if (Source is null || ImageRect() is not { } image)
        {
            return;
        }

        dc.DrawImage(Source, image);
        var crop = CropToScreen(image);

        // Darken outside the crop.
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(image), new RectangleGeometry(crop));
        dc.DrawGeometry(Shade, null, outside);
        dc.DrawRectangle(null, Border, crop);
        for (var i = 1; i <= 2; i++)
        {
            var x = crop.Left + (crop.Width * i / 3);
            var y = crop.Top + (crop.Height * i / 3);
            dc.DrawLine(Thirds, new Point(x, crop.Top), new Point(x, crop.Bottom));
            dc.DrawLine(Thirds, new Point(crop.Left, y), new Point(crop.Right, y));
        }

        foreach (var corner in new[] { crop.TopLeft, crop.TopRight, crop.BottomLeft, crop.BottomRight })
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(corner.X - (HandleSize / 2), corner.Y - (HandleSize / 2), HandleSize, HandleSize));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        Focus();
        if (ImageRect() is not { } image)
        {
            return;
        }

        var p = e.GetPosition(this);
        _drag = HitTest(CropToScreen(image), p);
        if (_drag != DragMode.None)
        {
            _dragStart = p;
            _startCrop = Crop;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (ImageRect() is not { } image)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (_drag == DragMode.None)
        {
            Cursor = CursorFor(HitTest(CropToScreen(image), p));
            return;
        }

        var dx = (p.X - _dragStart.X) / image.Width;
        var dy = (p.Y - _dragStart.Y) / image.Height;
        Crop = Apply(_startCrop, _drag, dx, dy, LockedRatio);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = DragMode.None;
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.05 : 0.01;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };

        if (dx == 0 && dy == 0)
        {
            return;
        }

        var mode = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? DragMode.Right | DragMode.Bottom : DragMode.Move;
        Crop = Apply(Crop, mode, dx, dy, LockedRatio);
        e.Handled = true;
    }

    /// <summary>The crop after dragging by (dx, dy) in normalised units. Pure, so it is unit-tested.</summary>
    internal static CropRect Apply(CropRect start, DragMode mode, double dx, double dy, double lockedRatio)
    {
        if (mode == DragMode.Move)
        {
            return CropMath.Clamp(start.X + dx, start.Y + dy, start.Width, start.Height);
        }

        double left = start.X, top = start.Y, right = start.X + start.Width, bottom = start.Y + start.Height;
        if (mode.HasFlag(DragMode.Left))
        {
            left = Math.Clamp(left + dx, 0, right - CropRect.MinSide);
        }

        if (mode.HasFlag(DragMode.Right))
        {
            right = Math.Clamp(right + dx, left + CropRect.MinSide, 1);
        }

        if (mode.HasFlag(DragMode.Top))
        {
            top = Math.Clamp(top + dy, 0, bottom - CropRect.MinSide);
        }

        if (mode.HasFlag(DragMode.Bottom))
        {
            bottom = Math.Clamp(bottom + dy, top + CropRect.MinSide, 1);
        }

        double width = right - left, height = bottom - top;
        if (lockedRatio > 0)
        {
            // Keep the ratio: the dragged dimension leads; shrink to fit inside the frame from the fixed corner.
            var horizontal = mode.HasFlag(DragMode.Left) || mode.HasFlag(DragMode.Right);
            var vertical = mode.HasFlag(DragMode.Top) || mode.HasFlag(DragMode.Bottom);
            if (horizontal && (!vertical || Math.Abs(dx) >= Math.Abs(dy)))
            {
                height = width / lockedRatio;
            }
            else
            {
                width = height * lockedRatio;
            }

            var maxWidth = mode.HasFlag(DragMode.Left) ? right : 1 - left;
            var maxHeight = mode.HasFlag(DragMode.Top) ? bottom : 1 - top;
            var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
            width *= scale;
            height *= scale;
            left = mode.HasFlag(DragMode.Left) ? right - width : left;
            top = mode.HasFlag(DragMode.Top) ? bottom - height : top;
        }

        return CropMath.Clamp(left, top, width, height);
    }

    private static DragMode HitTest(Rect crop, Point p)
    {
        const double grip = HandleSize;
        var mode = DragMode.None;
        var withinY = p.Y >= crop.Top - grip && p.Y <= crop.Bottom + grip;
        var withinX = p.X >= crop.Left - grip && p.X <= crop.Right + grip;
        if (withinY && Math.Abs(p.X - crop.Left) <= grip)
        {
            mode |= DragMode.Left;
        }
        else if (withinY && Math.Abs(p.X - crop.Right) <= grip)
        {
            mode |= DragMode.Right;
        }

        if (withinX && Math.Abs(p.Y - crop.Top) <= grip)
        {
            mode |= DragMode.Top;
        }
        else if (withinX && Math.Abs(p.Y - crop.Bottom) <= grip)
        {
            mode |= DragMode.Bottom;
        }

        return mode == DragMode.None && crop.Contains(p) ? DragMode.Move : mode;
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.Move => Cursors.SizeAll,
        DragMode.Left or DragMode.Right => Cursors.SizeWE,
        DragMode.Top or DragMode.Bottom => Cursors.SizeNS,
        DragMode.Left | DragMode.Top or DragMode.Right | DragMode.Bottom => Cursors.SizeNWSE,
        DragMode.Right | DragMode.Top or DragMode.Left | DragMode.Bottom => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    private Rect? ImageRect()
    {
        if (Source is not { Width: > 0, Height: > 0 } source || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(ActualWidth / source.Width, ActualHeight / source.Height);
        double w = source.Width * scale, h = source.Height * scale;
        return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
    }

    private Rect CropToScreen(Rect image) => new(
        image.X + (Crop.X * image.Width), image.Y + (Crop.Y * image.Height), Crop.Width * image.Width, Crop.Height * image.Height);

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
