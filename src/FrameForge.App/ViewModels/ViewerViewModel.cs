using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// Single-photo viewer. Only the current photo is decoded at full resolution; moving to another
/// photo cancels any pending load, and at most one decode runs at a time so rapid key-repeat
/// cannot stack up full-resolution bitmaps.
/// </summary>
public sealed partial class ViewerViewModel(IPhotoBitmapLoader loader, ILogger<ViewerViewModel> logger) : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private IReadOnlyList<PhotoTileViewModel> _items = [];
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(PreviousCommand), nameof(FirstCommand), nameof(LastCommand))]
    private int _currentIndex = -1;

    [ObservableProperty]
    private PhotoTileViewModel? _current;

    [ObservableProperty]
    private BitmapSource? _image;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

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

    public void Close()
    {
        CancelLoad();
        Image = null; // release the full-resolution bitmap
        ErrorMessage = null;
        IsLoading = false;
    }

    public void Dispose()
    {
        CancelLoad();
        _decodeGate.Dispose();
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

            var path = tile.FilePath;
            var bitmap = await Task.Run(() => loader.LoadFull(path, token), token).ConfigureAwait(true);
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
