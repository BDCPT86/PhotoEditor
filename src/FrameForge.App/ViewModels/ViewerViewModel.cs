using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Analysis;
using FrameForge.App.Services;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Single-photo viewer. Photos are shown at screen size (<see cref="DisplayLongSide"/>), and the next and previous
/// photos are loaded ahead (<see cref="ViewerImageCache"/>), so stepping through them doesn't wait for a decode. The
/// full-resolution image is loaded only when the zoom needs it (<see cref="WantsDetail"/>), for the current photo only.
/// Edited photos are shown with their edit applied. While the Develop panel is open, the live preview is shown instead.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject, IDisposable
{
    /// <summary>Used until the view reports its size: enough for a 1440p screen.</summary>
    public const int DefaultDisplayLongSide = 2560;

    private readonly IPhotoBitmapLoader _loader;
    private readonly IEditRenderService _renderer;
    private readonly ILogger<ViewerViewModel> _logger;
    private readonly ViewerImageCache _cache;
    private IReadOnlyList<PhotoTileViewModel> _items = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _detailCts;
    private PhotoTileViewModel? _detailFor;
    private int _direction = 1;

    private readonly IFaceLookup _faces;
    private int _faceIndex = -1;

    public ViewerViewModel(IPhotoBitmapLoader loader, IEditRenderService renderer, DevelopViewModel develop, IFaceLookup faces, ILogger<ViewerViewModel> logger)
    {
        _loader = loader;
        _renderer = renderer;
        _faces = faces;
        _logger = logger;
        _cache = new ViewerImageCache(LoadForDisplayAsync);
        Develop = develop;
        Develop.PropertyChanged += OnDevelopChanged;
        Histogram = new HistogramViewModel(logger);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DisplayImage) or nameof(ShowHistogram))
            {
                UpdateHistogram();
            }

            if (e.PropertyName is nameof(DisplayImage) or nameof(ShowClipping))
            {
                UpdateClipping();
            }
        };
    }

    /// <summary>Longest side, in device pixels, that photos are loaded at for the fit view. Set by the view from its size.</summary>
    public int DisplayLongSide { get; set; } = DefaultDisplayLongSide;

    /// <summary>Full size of the photo shown (after its edit), which zoom refers to; 0 when unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullWidth))]
    private int _fullWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayFullHeight))]
    private int _fullHeight;

    /// <summary>Full size for the image area: 0 (the bitmap's own size) while developing.</summary>
    public int DisplayFullWidth => Develop.IsOpen ? 0 : FullWidth;

    public int DisplayFullHeight => Develop.IsOpen ? 0 : FullHeight;

    /// <summary>Which photo the image area shows, so a sharper copy keeps the zoom; null while developing.</summary>
    public object? DisplayKey => Develop.IsOpen ? null : Current?.Id;

    /// <summary>Set by the image area while the zoom enlarges the screen-size copy: loads the full-resolution image.</summary>
    [ObservableProperty]
    private bool _wantsDetail;

    /// <summary>What the loading line says.</summary>
    [ObservableProperty]
    private string _loadingText = "Loading…";

    public DevelopViewModel Develop { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(PreviousCommand), nameof(FirstCommand), nameof(LastCommand))]
    private int _currentIndex = -1;

    [ObservableProperty]
    private PhotoTileViewModel? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private BitmapSource? _image;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>The face the 100 % view should centre on (zoom to faces), or null.</summary>
    [ObservableProperty]
    private Controls.ZoomFocus? _faceFocus;

    /// <summary>"Face 2 of 4", or why there are no faces to step through; null when not in use.</summary>
    [ObservableProperty]
    private string? _faceStatus;

    /// <summary>
    /// Steps the 100 % view to the next (+1) or previous (−1) face, left to right, using the stored face boxes mapped
    /// through the photo's edit. Not while developing (the preview is not full size).
    /// </summary>
    public void StepFace(int direction)
    {
        if (Current is not { } tile || Develop.IsOpen)
        {
            return;
        }

        if (!_faces.HasFaceData(tile.Id))
        {
            FaceStatus = "No face data: turn on faces (Settings → AI models) and run Analyze";
            return;
        }

        var targets = FaceTargets(tile);
        if (targets.Count == 0)
        {
            FaceStatus = "No faces in this photo";
            return;
        }

        _faceIndex = (((_faceIndex + direction) % targets.Count) + targets.Count) % targets.Count;
        var (x, y, w, h) = targets[_faceIndex];
        FaceFocus = new Controls.ZoomFocus(x, y, w, h);
        FaceStatus = string.Create(CultureInfo.CurrentCulture, $"Face {_faceIndex + 1} of {targets.Count}");
    }

    /// <summary>Counted faces, left to right, as normalised rectangles of the displayed (edited) photo.</summary>
    internal List<(double X, double Y, double W, double H)> FaceTargets(PhotoTileViewModel tile)
    {
        var minScore = _faces.FaceMinScore;
        var recipe = tile.Recipe;
        var (sw, sh) = EditRenderService.OrientedSize(tile.Photo) ?? (1000, 1000);
        var geometry = recipe.HasGeometry ? new Rendering.EditGeometry(sw, sh, recipe) : null;
        var result = new List<(double, double, double, double)>();
        foreach (var f in _faces.FacesOf(tile.Id).Where(f => f.Score >= minScore))
        {
            if (geometry is null)
            {
                result.Add((f.X, f.Y, f.Width, f.Height));
                continue;
            }

            var (u1, v1) = geometry.ToOutput(f.X * sw, f.Y * sh);
            var (u2, v2) = geometry.ToOutput((f.X + f.Width) * sw, (f.Y + f.Height) * sh);
            double l = Math.Min(u1, u2), t = Math.Min(v1, v2), r = Math.Max(u1, u2), b = Math.Max(v1, v2);
            var (cx, cy) = ((l + r) / 2, (t + b) / 2);
            if (cx is >= 0 and <= 1 && cy is >= 0 and <= 1)
            {
                result.Add((l, t, r - l, b - t)); // cropped-away faces are skipped
            }
        }

        return result.OrderBy(r => r.Item1).ToList();
    }

    /// <summary>What the image area shows: the Develop preview while developing, otherwise the full-size photo.</summary>
    public BitmapSource? DisplayImage => ShowClipping && _clippingImage is { } painted ? painted : UnpaintedImage;

    /// <summary>The image on screen before the clipping overlay: the Develop preview while developing, otherwise the photo.</summary>
    private BitmapSource? UnpaintedImage => Develop.IsOpen ? Develop.DisplayImage : Image;

    /// <summary>
    /// What the histogram counts and the overlay paints: the image on screen, but in Develop the edit without the mask
    /// overlay (so its red tint is never counted or painted over), or the original or an Auto option while one of those
    /// is shown instead.
    /// </summary>
    private object? OnScreenSource() => Develop.IsOpen
        ? Develop.ShowOriginal || Develop.FocusedSuggestion is not null
            ? Develop.DisplayImage
            : (object?)Develop.RenderedPixels ?? Develop.DisplayImage
        : Image;

    /// <summary>The histogram of the image on screen: in Develop's panel, and over the photo when <see cref="ShowHistogram"/> (H).</summary>
    public HistogramViewModel Histogram { get; }

    /// <summary>Show the histogram over the photo (H, remembered). Develop always shows its own at the top of its panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFloatingHistogram))]
    private bool _showHistogram;

    /// <summary>The histogram over the photo: on request, and not while Develop shows its own.</summary>
    public bool ShowFloatingHistogram => ShowHistogram && !Develop.IsOpen;

    /// <summary>Counts what is on screen, only while a histogram is shown.</summary>
    private void UpdateHistogram() => Histogram.Show(Develop.IsOpen || ShowHistogram ? OnScreenSource() : null);

    /// <summary>
    /// The clipping overlay (J): highlights painted red, shadows blue, on a copy of the image on screen. Display only;
    /// never part of an export. Not remembered: it is a quick look, and starts off each time FrameForge starts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayImage))]
    private bool _showClipping;

    private BitmapSource? _clippingImage;
    private object? _clippingFor;
    private object? _clippingPending;
    private bool _painting;

    /// <summary>The painting in progress, if any (for tests).</summary>
    internal Task ClippingWork { get; private set; } = Task.CompletedTask;

    /// <summary>Paints the overlay for what is on screen; while one paints, newer images replace each other.</summary>
    private void UpdateClipping()
    {
        var source = ShowClipping ? OnScreenSource() : null;
        if (ReferenceEquals(source, _clippingFor))
        {
            return;
        }

        _clippingFor = source;
        if (source is null)
        {
            _clippingPending = null;
            SetClippingImage(null);
            return;
        }

        _clippingPending = source;
        if (!_painting)
        {
            ClippingWork = PaintClippingAsync();
        }
    }

    private async Task PaintClippingAsync()
    {
        _painting = true;
        try
        {
            while (_clippingPending is { } source)
            {
                _clippingPending = null;
                BitmapSource? painted;
                try
                {
                    painted = await Task.Run(() => BitmapConversions.ToBitmap(ClippingPainter.Paint(
                        source as AnalysisImage ?? BitmapConversions.ToBuffer((BitmapSource)source)))).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidCastException)
                {
                    LogClippingFailed(ex);
                    painted = null;
                }

                if (ReferenceEquals(source, _clippingFor))
                {
                    SetClippingImage(painted);
                }
            }
        }
        finally
        {
            _painting = false;
        }
    }

    private void SetClippingImage(BitmapSource? image)
    {
        if (!ReferenceEquals(image, _clippingImage))
        {
            _clippingImage = image;
            OnPropertyChanged(nameof(DisplayImage));
        }
    }

    public string PositionText => _items.Count == 0
        ? string.Empty
        : string.Create(CultureInfo.CurrentCulture, $"{CurrentIndex + 1:N0} / {_items.Count:N0}");

    /// <summary>The most recent load; exposed so tests can await it.</summary>
    internal Task CurrentLoad { get; private set; } = Task.CompletedTask;

    /// <summary>Raised when the user asks to leave the viewer (Esc or the back button).</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void Open(IReadOnlyList<PhotoTileViewModel> items, int index)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        CurrentIndex = -1;
        MoveTo(index);
    }

    /// <summary>Closes the viewer; any pending Develop change is saved first. The viewer's images are released.</summary>
    public async Task CloseAsync()
    {
        if (Develop.IsOpen)
        {
            await Develop.CloseAsync().ConfigureAwait(true);
        }

        CancelLoad();
        CancelDetail();
        _cache.Clear();
        Image = null;
        ErrorMessage = null;
        IsLoading = false;
        MemoryRelief.AfterLargeWork();
    }

    /// <summary>Opens or closes the Develop panel for the current photo.</summary>
    [RelayCommand]
    private async Task ToggleDevelopAsync()
    {
        if (Develop.IsOpen)
        {
            await Develop.CloseAsync().ConfigureAwait(true);
            if (Current is { } shown)
            {
                CurrentLoad = LoadCurrentAsync(shown); // show the final edit at full resolution
            }
        }
        else if (Current is { } tile)
        {
            await Develop.OpenAsync(tile).ConfigureAwait(true);
        }
    }

    public void Dispose()
    {
        CancelLoad();
        CancelDetail();
        _cache.Clear();
        Develop.PropertyChanged -= OnDevelopChanged;
    }

    private void OnDevelopChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DevelopViewModel.DisplayImage) or nameof(DevelopViewModel.IsOpen))
        {
            // Size and key before the image: the image area reads them when the image changes.
            OnPropertyChanged(nameof(DisplayFullWidth));
            OnPropertyChanged(nameof(DisplayFullHeight));
            OnPropertyChanged(nameof(DisplayKey));
            OnPropertyChanged(nameof(DisplayImage));
            OnPropertyChanged(nameof(ShowFloatingHistogram));
        }
    }

    private bool CanGoNext() => CurrentIndex < _items.Count - 1;

    private bool CanGoPrevious() => CurrentIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => MoveTo(CurrentIndex + 1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => MoveTo(CurrentIndex - 1);

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void First() => MoveTo(0);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Last() => MoveTo(_items.Count - 1);

    private void MoveTo(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _items.Count - 1);
        if (index == CurrentIndex)
        {
            return;
        }

        _direction = CurrentIndex < 0 || index >= CurrentIndex ? 1 : -1;
        CurrentIndex = index;
        Current = _items[index];
        _faceIndex = -1;
        FaceFocus = null;
        FaceStatus = null;
        CancelDetail();
        CurrentLoad = LoadCurrentAsync(Current);
        if (Develop.IsOpen)
        {
            _ = Develop.OpenAsync(Current); // saves the previous photo's pending change first
        }
    }

    /// <summary>The photo shown, then the next one in the direction of travel, then the one behind.</summary>
    private IEnumerable<PhotoTileViewModel> Neighbourhood(int index)
    {
        yield return _items[index];
        foreach (var i in new[] { index + _direction, index - _direction })
        {
            if (i >= 0 && i < _items.Count)
            {
                yield return _items[i];
            }
        }
    }

    /// <summary>
    /// Cancels the current load. The token source is not disposed here: the superseded load may
    /// still be using its token; that load disposes its own source when it finishes.
    /// </summary>
    private void CancelLoad()
    {
        _loadCts?.Cancel();
        _loadCts = null;
    }

    private void CancelDetail()
    {
        _detailCts?.Cancel();
        _detailCts = null;
        _detailFor = null;
    }

    /// <summary>Loads one photo at screen size, with its edit; runs on a worker thread.</summary>
    private async Task<BitmapSource> LoadForDisplayAsync(PhotoTileViewModel tile, int longSide, CancellationToken token)
    {
        var photo = tile.Photo;
        var recipe = tile.Recipe;
        return recipe.IsNeutral
            ? await Task.Run(() => _loader.LoadForDisplay(photo.FilePath, longSide, token), token).ConfigureAwait(false)
            : BitmapConversions.ToBitmap(await _renderer.RenderAsync(photo, recipe, longSide, token).ConfigureAwait(false));
    }

    private async Task LoadCurrentAsync(PhotoTileViewModel tile)
    {
        CancelLoad();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;
        var side = DisplayLongSide;
        var index = CurrentIndex;

        ErrorMessage = null;
        LoadingText = "Loading…";
        var ready = _cache.TryGet(tile, side);
        var loading = ready is null ? _cache.GetAsync(tile, side, token) : null;

        // Then load ahead: the next photo in the direction of travel, then the one behind. Anything else is cancelled.
        _cache.Want(Neighbourhood(index), side);
        if (loading is not null)
        {
            Image = null;
            IsLoading = true;
        }

        try
        {
            Show(tile, ready ?? await loading!.ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by navigation or closed.
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogLoadFailed(ex, tile.FilePath);
            ErrorMessage = tile.OfflineReason is { } offline
                ? offline + "\n" + tile.FilePath + "\nConnect the drive, or use Locate folder… on the folder in the sidebar."
                : "This file no longer exists:\n" + tile.FilePath;
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, tile.FilePath);
            ErrorMessage = "This photo can't be displayed.\n" + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
                _loadCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>Shows an image of the current photo; its full size first, so the image area scales it correctly.</summary>
    private void Show(PhotoTileViewModel tile, BitmapSource image)
    {
        var (width, height) = EditRenderService.FullOutputSize(tile.Photo, tile.Recipe) ?? (image.PixelWidth, image.PixelHeight);
        FullWidth = width;
        FullHeight = height;
        OnPropertyChanged(nameof(DisplayKey));
        Image = image;
    }

    partial void OnWantsDetailChanged(bool value)
    {
        if (value)
        {
            RequestDetail();
        }
    }

    /// <summary>
    /// A sharper image of the current photo: the screen-size one if what is shown is smaller (it was loaded before
    /// the window had its size), otherwise the full-resolution one.
    /// </summary>
    private void RequestDetail()
    {
        if (Current is not { } tile || Develop.IsOpen || _detailFor == tile || Image is not { } shown)
        {
            return;
        }

        var shownLongSide = Math.Max(shown.PixelWidth, shown.PixelHeight);
        var fullLongSide = Math.Max(FullWidth, FullHeight);
        if (shownLongSide >= fullLongSide)
        {
            return;
        }

        if (shownLongSide < DisplayLongSide && DisplayLongSide < fullLongSide)
        {
            _ = LoadScreenSizeAsync(tile, shownLongSide);
        }
        else
        {
            _ = LoadDetailAsync(tile);
        }
    }

    private async Task LoadScreenSizeAsync(PhotoTileViewModel tile, int shownLongSide)
    {
        var token = _loadCts?.Token ?? CancellationToken.None;
        try
        {
            var image = await _cache.GetAsync(tile, DisplayLongSide, token).ConfigureAwait(true);
            if (Math.Max(image.PixelWidth, image.PixelHeight) <= shownLongSide)
            {
                await LoadDetailAsync(tile).ConfigureAwait(true); // no sharper at screen size (a small original)
                return;
            }

            if (Current == tile && !token.IsCancellationRequested)
            {
                Show(tile, image);
                if (WantsDetail)
                {
                    RequestDetail(); // zoomed in further than the screen-size image covers
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Moved on or closed.
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, tile.FilePath); // the smaller image stays
        }
    }

    /// <summary>The full-resolution image of the current photo, for zooming in; replaces the screen-size one.</summary>
    private async Task LoadDetailAsync(PhotoTileViewModel tile)
    {
        CancelDetail();
        var cts = new CancellationTokenSource();
        _detailCts = cts;
        _detailFor = tile;
        var token = cts.Token;
        LoadingText = "Loading full resolution…";
        IsLoading = true;
        try
        {
            var photo = tile.Photo;
            var recipe = tile.Recipe;
            var full = recipe.IsNeutral
                ? await Task.Run(() => _loader.LoadFull(photo.FilePath, token), token).ConfigureAwait(true)
                : BitmapConversions.ToBitmap(await _renderer.RenderAsync(photo, recipe, null, token).ConfigureAwait(true));
            if (!token.IsCancellationRequested && Current == tile)
            {
                Show(tile, full);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Moved on or closed.
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            // The screen-size image stays; zooming just shows it enlarged.
            LogLoadFailed(ex, tile.FilePath);
            _detailFor = null;
        }
        finally
        {
            if (ReferenceEquals(_detailCts, cts))
            {
                IsLoading = false;
                _detailCts = null;
            }

            cts.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The clipping overlay could not be painted for the image on screen.")]
    private partial void LogClippingFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Viewer could not load {FilePath}")]
    private partial void LogLoadFailed(Exception exception, string filePath);
}
