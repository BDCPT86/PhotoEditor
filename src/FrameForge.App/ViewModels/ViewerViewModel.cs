using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Editing;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Single-photo viewer. Only the current photo is decoded at full resolution; moving to another
/// photo cancels any pending load, and at most one decode runs at a time so rapid key-repeat
/// cannot stack up full-resolution bitmaps. Edited photos are shown with their edit applied.
/// While the Develop panel is open, the live preview is shown instead.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject, IDisposable
{
    private readonly IPhotoBitmapLoader _loader;
    private readonly IEditRenderService _renderer;
    private readonly ILogger<ViewerViewModel> _logger;
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private IReadOnlyList<PhotoTileViewModel> _items = [];
    private CancellationTokenSource? _loadCts;

    public ViewerViewModel(IPhotoBitmapLoader loader, IEditRenderService renderer, DevelopViewModel develop, ILogger<ViewerViewModel> logger)
    {
        _loader = loader;
        _renderer = renderer;
        _logger = logger;
        Develop = develop;
        Develop.PropertyChanged += OnDevelopChanged;
    }

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

    /// <summary>What the image area shows: the Develop preview while developing, otherwise the full-size photo.</summary>
    public BitmapSource? DisplayImage => Develop.IsOpen ? Develop.DisplayImage : Image;

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

    /// <summary>Closes the viewer; any pending Develop change is saved first.</summary>
    public async Task CloseAsync()
    {
        if (Develop.IsOpen)
        {
            await Develop.CloseAsync().ConfigureAwait(true);
        }

        CancelLoad();
        Image = null; // release the full-resolution bitmap
        ErrorMessage = null;
        IsLoading = false;
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
        _decodeGate.Dispose();
        Develop.PropertyChanged -= OnDevelopChanged;
    }

    private void OnDevelopChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DevelopViewModel.DisplayImage) or nameof(DevelopViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(DisplayImage));
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

        CurrentIndex = index;
        Current = _items[index];
        CurrentLoad = LoadCurrentAsync(Current);
        if (Develop.IsOpen)
        {
            _ = Develop.OpenAsync(Current); // saves the previous photo's pending change first
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

    private async Task LoadCurrentAsync(PhotoTileViewModel tile)
    {
        CancelLoad();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        Image = null;
        ErrorMessage = null;
        IsLoading = true;

        var acquired = false;
        try
        {
            await _decodeGate.WaitAsync(token).ConfigureAwait(true);
            acquired = true;

            var photo = tile.Photo;
            var recipe = tile.Recipe;
            var bitmap = recipe.IsNeutral
                ? await Task.Run(() => _loader.LoadFull(photo.FilePath, token), token).ConfigureAwait(true)
                : BitmapConversions.ToBitmap(await _renderer.RenderAsync(photo, recipe, null, token).ConfigureAwait(true));
            if (!token.IsCancellationRequested)
            {
                Image = bitmap;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by navigation or closed.
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogLoadFailed(ex, tile.FilePath);
            ErrorMessage = "This file no longer exists:\n" + tile.FilePath;
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, tile.FilePath);
            ErrorMessage = "This photo can't be displayed.\n" + ex.Message;
        }
        finally
        {
            if (acquired)
            {
                _decodeGate.Release();
            }

            if (ReferenceEquals(_loadCts, cts))
            {
                IsLoading = false;
                _loadCts = null;
            }

            cts.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Viewer could not load {FilePath}")]
    private partial void LogLoadFailed(Exception exception, string filePath);
}
