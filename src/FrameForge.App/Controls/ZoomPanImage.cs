using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameForge.App.Controls;

/// <summary>An area to zoom to, normalised to the image (0–1). A new instance re-triggers even for the same area.</summary>
public sealed class ZoomFocus(double x, double y, double width, double height)
{
    public double X { get; } = x;

    public double Y { get; } = y;

    public double Width { get; } = width;

    public double Height { get; } = height;
}

/// <summary>A click on the image in point mode: normalised position (0–1) and whether it adds (true) or removes.</summary>
public sealed record ImagePoint(double U, double V, bool Include);

/// <summary>
/// Displays a bitmap with fit / 100% / free zoom and drag-to-pan.
/// Mouse: wheel zooms around the cursor, drag pans, double-click toggles fit ↔ 100% at the cursor.
/// Keyboard (when focused): Z toggles fit ↔ 100%, + / − zoom. (Digits are left for culling shortcuts.)
/// This is presentation behaviour only; it holds no application state.
/// </summary>
public sealed class ZoomPanImage : Border
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(ZoomPanImage),
        new FrameworkPropertyMetadata(null, (d, _) => ((ZoomPanImage)d).OnSourceChanged()));

    /// <summary>Set to a new <see cref="ZoomFocus"/> to show that area of the image at 100 %, centred.</summary>
    public static readonly DependencyProperty FocusRequestProperty = DependencyProperty.Register(
        nameof(FocusRequest), typeof(ZoomFocus), typeof(ZoomPanImage),
        new FrameworkPropertyMetadata(null, (d, e) => ((ZoomPanImage)d).OnFocusRequested(e.NewValue as ZoomFocus)));

    /// <summary>
    /// When true, a click that does not drag raises <see cref="PointCommand"/> with the clicked image position: a plain
    /// click adds, Alt+click or a right-click removes. Dragging still pans and the wheel still zooms; double-click
    /// zoom is off (use Z), so a quick second click is a second point.
    /// </summary>
    public static readonly DependencyProperty IsPointModeProperty = DependencyProperty.Register(
        nameof(IsPointMode), typeof(bool), typeof(ZoomPanImage), new FrameworkPropertyMetadata(false, (d, _) => ((ZoomPanImage)d).UpdateCursor()));

    public static readonly DependencyProperty PointCommandProperty = DependencyProperty.Register(
        nameof(PointCommand), typeof(ICommand), typeof(ZoomPanImage), new FrameworkPropertyMetadata(null));

    /// <summary>
    /// The photo's full size in pixels when <see cref="Source"/> is a smaller copy (0 = <see cref="Source"/> is full
    /// size). Zoom percentages and 100 % refer to this size. Set it before <see cref="Source"/>.
    /// </summary>
    public static readonly DependencyProperty FullPixelWidthProperty = DependencyProperty.Register(
        nameof(FullPixelWidth), typeof(int), typeof(ZoomPanImage), new FrameworkPropertyMetadata(0));

    /// <inheritdoc cref="FullPixelWidthProperty"/>
    public static readonly DependencyProperty FullPixelHeightProperty = DependencyProperty.Register(
        nameof(FullPixelHeight), typeof(int), typeof(ZoomPanImage), new FrameworkPropertyMetadata(0));

    /// <summary>
    /// Identifies what is shown (for example the photo). When a new <see cref="Source"/> arrives with the same key and
    /// the same full size, such as a sharper copy, the zoom and position are kept. Null: only kept while pointing.
    /// </summary>
    public static readonly DependencyProperty SourceKeyProperty = DependencyProperty.Register(
        nameof(SourceKey), typeof(object), typeof(ZoomPanImage), new FrameworkPropertyMetadata(null));

    /// <summary>True while the current zoom shows <see cref="Source"/> enlarged beyond its pixels, so a sharper copy would show more.</summary>
    public static readonly DependencyProperty NeedsDetailProperty = DependencyProperty.Register(
        nameof(NeedsDetail), typeof(bool), typeof(ZoomPanImage), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>A drag shorter than this (DIPs) still counts as a click.</summary>
    private const double ClickSlop = 4;

    private static readonly DependencyPropertyKey ZoomTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ZoomText), typeof(string), typeof(ZoomPanImage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ZoomTextProperty = ZoomTextPropertyKey.DependencyProperty;

    private const double WheelStep = 1.25;
    private const double KeyStep = 1.5;

    private readonly Image _image = new()
    {
        Stretch = Stretch.Fill,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        RenderTransformOrigin = new Point(0, 0),
    };

    private readonly MatrixTransform _transform = new();
    private double _zoom = 1.0;
    private Point _offset;
    private bool _isFit = true;
    private Point? _dragStart;
    private Point _dragStartOffset;
    private bool _moved;
    private object? _shownKey;

    public ZoomPanImage()
    {
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent; // receive mouse input over empty areas
        _image.RenderTransform = _transform;

        // Host the image in a Canvas: a Canvas arranges children at their full desired size and
        // never applies a layout clip. Placing the (often larger-than-viewport) image directly in
        // the Border would clip it to the viewport *before* the zoom transform scales it.
        var canvas = new Canvas();
        canvas.Children.Add(_image);
        Child = canvas;
        SizeChanged += (_, _) => UpdateLayoutForCurrentMode();
    }

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ZoomFocus? FocusRequest
    {
        get => (ZoomFocus?)GetValue(FocusRequestProperty);
        set => SetValue(FocusRequestProperty, value);
    }

    public bool IsPointMode
    {
        get => (bool)GetValue(IsPointModeProperty);
        set => SetValue(IsPointModeProperty, value);
    }

    public ICommand? PointCommand
    {
        get => (ICommand?)GetValue(PointCommandProperty);
        set => SetValue(PointCommandProperty, value);
    }

    public int FullPixelWidth
    {
        get => (int)GetValue(FullPixelWidthProperty);
        set => SetValue(FullPixelWidthProperty, value);
    }

    public int FullPixelHeight
    {
        get => (int)GetValue(FullPixelHeightProperty);
        set => SetValue(FullPixelHeightProperty, value);
    }

    public object? SourceKey
    {
        get => GetValue(SourceKeyProperty);
        set => SetValue(SourceKeyProperty, value);
    }

    public bool NeedsDetail
    {
        get => (bool)GetValue(NeedsDetailProperty);
        set => SetValue(NeedsDetailProperty, value);
    }

    /// <summary>"Fit" or a percentage such as "100%".</summary>
    public string ZoomText => (string)GetValue(ZoomTextProperty);

    /// <summary>The size zoom refers to: the full photo, even when a smaller copy is shown.</summary>
    private (int Width, int Height) LogicalSize =>
        Source is not { } s ? default
            : FullPixelWidth > 0 && FullPixelHeight > 0 ? (FullPixelWidth, FullPixelHeight)
            : (s.PixelWidth, s.PixelHeight);

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>DIPs per image pixel.</summary>
    private double Scale => _zoom / DpiScale;

    private Size ViewportSize => new(ActualWidth, ActualHeight);

    private Size ContentSize => Source is null ? default : new Size(LogicalSize.Width * Scale, LogicalSize.Height * Scale);

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Source is null)
        {
            return;
        }

        ZoomTo(_zoom * (e.Delta > 0 ? WheelStep : 1 / WheelStep), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Source is null)
        {
            return;
        }

        if (e.ClickCount == 2 && !IsPointMode)
        {
            ToggleFitAnd100(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(this);
        _moved = false;
        _dragStartOffset = _offset;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start || !IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(this);
        _moved |= Math.Abs(current.X - start.X) > ClickSlop || Math.Abs(current.Y - start.Y) > ClickSlop;
        SetOffset(new Point(_dragStartOffset.X + (current.X - start.X), _dragStartOffset.Y + (current.Y - start.Y)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var wasClick = _dragStart is not null && !_moved;
        _dragStart = null;
        ReleaseMouseCapture();
        if (wasClick && IsPointMode)
        {
            RaisePoint(e.GetPosition(this), include: (Keyboard.Modifiers & ModifierKeys.Alt) == 0);
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (IsPointMode && Source is not null)
        {
            RaisePoint(e.GetPosition(this), include: false);
            e.Handled = true;
        }
    }

    /// <summary>Alt+click: WPF sends Alt to the menu as a system key; keep it here while pointing.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsPointMode && e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
        {
            e.Handled = true;
        }
    }

    /// <summary>Viewport position → normalised image position, through the current zoom and pan.</summary>
    internal static (double U, double V) ToImage(Point position, Point offset, double scale, int pixelWidth, int pixelHeight) =>
        (((position.X - offset.X) / scale) / pixelWidth, ((position.Y - offset.Y) / scale) / pixelHeight);

    private void RaisePoint(Point position, bool include)
    {
        if (Source is not { } source)
        {
            return;
        }

        var (u, v) = ToImage(position, _offset, Scale, LogicalSize.Width, LogicalSize.Height);
        var point = new ImagePoint(u, v, include);
        if (u is >= 0 and <= 1 && v is >= 0 and <= 1 && PointCommand?.CanExecute(point) == true)
        {
            PointCommand.Execute(point);
        }
    }

    private void UpdateCursor() => SetOffset(_offset);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Source is null || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        switch (e.Key)
        {
            case Key.Z:
                ToggleFitAnd100(center);
                break;
            case Key.OemPlus or Key.Add:
                ZoomTo(_zoom * KeyStep, center);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomTo(_zoom / KeyStep, center);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnSourceChanged()
    {
        // A new copy of the same picture at the same full size keeps the zoom and position: a re-render while pointing,
        // or a sharper copy swapped in after zooming. Without a key, only while pointing (the old rule).
        var key = SourceKey;
        var sameKey = key is null ? IsPointMode : Equals(key, _shownKey);
        var (width, height) = LogicalSize;
        var keepView = sameKey && !_isFit && Source is not null
            && Math.Abs(_image.Width - width) < 0.5 && Math.Abs(_image.Height - height) < 0.5;

        // The image element always has the full size; a smaller bitmap is stretched to it.
        _image.Width = width;
        _image.Height = height;
        _image.Source = Source;
        _shownKey = key;
        if (keepView)
        {
            SetOffset(_offset);
        }
        else
        {
            Fit();
        }
    }

    private void OnFocusRequested(ZoomFocus? focus)
    {
        if (focus is null || Source is not { } source || ActualWidth <= 0)
        {
            return;
        }

        // 100 % (never below fit), centred on the requested area.
        _zoom = Math.Max(1.0, CurrentFitZoom());
        _isFit = Math.Abs(_zoom - CurrentFitZoom()) < 1e-6;
        var point = new Point((focus.X + (focus.Width / 2)) * LogicalSize.Width * Scale, (focus.Y + (focus.Height / 2)) * LogicalSize.Height * Scale);
        SetOffset(new Point((ActualWidth / 2) - point.X, (ActualHeight / 2) - point.Y));
    }

    private void UpdateLayoutForCurrentMode()
    {
        if (_isFit)
        {
            Fit();
        }
        else
        {
            SetOffset(_offset);
        }
    }

    private double CurrentFitZoom() =>
        Source is null ? 1.0 : ZoomMath.FitZoom(LogicalSize.Width, LogicalSize.Height, ActualWidth, ActualHeight, DpiScale);

    private void Fit()
    {
        _isFit = true;
        _zoom = CurrentFitZoom();
        SetOffset(default); // ClampOffset centres content smaller than the viewport
    }

    private void ToggleFitAnd100(Point anchor)
    {
        if (_isFit && CurrentFitZoom() < 1.0)
        {
            ZoomTo(1.0, anchor);
        }
        else
        {
            Fit();
        }
    }

    private void ZoomTo(double requestedZoom, Point anchor)
    {
        var fitZoom = CurrentFitZoom();
        var newZoom = ZoomMath.ClampZoom(requestedZoom, fitZoom);
        var oldScale = Scale;
        _zoom = newZoom;
        _isFit = Math.Abs(newZoom - fitZoom) < 1e-6;
        SetOffset(ZoomMath.ZoomAt(_offset, anchor, oldScale, Scale));
    }

    private void SetOffset(Point offset)
    {
        _offset = ZoomMath.ClampOffset(offset, ContentSize, ViewportSize);

        var scale = Scale;
        _transform.Matrix = new Matrix(scale, 0, 0, scale, _offset.X, _offset.Y);

        // A smaller copy enlarged beyond its own pixels: a sharper copy would show more. (Device pixels per shown pixel.)
        var isSmallerCopy = Source is { } shown && shown.PixelWidth > 0 && LogicalSize.Width > shown.PixelWidth;
        var shownZoom = isSmallerCopy ? _zoom * LogicalSize.Width / Source!.PixelWidth : _zoom;
        var needsDetail = isSmallerCopy && shownZoom > 1.05;
        if (NeedsDetail != needsDetail)
        {
            NeedsDetail = needsDetail;
        }

        // Smooth when reducing; crisp pixels when inspecting the full image above 100 %.
        RenderOptions.SetBitmapScalingMode(_image, _zoom > 1.0 && !isSmallerCopy ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        Cursor = IsPointMode ? Cursors.Cross
            : ContentSize.Width > ActualWidth + 0.5 || ContentSize.Height > ActualHeight + 0.5 ? Cursors.SizeAll : null;
        SetValue(ZoomTextPropertyKey, Source is null ? string.Empty
            : _isFit ? string.Create(CultureInfo.CurrentCulture, $"Fit ({_zoom:P0})")
            : _zoom.ToString("P0", CultureInfo.CurrentCulture));
    }
}
