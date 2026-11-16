using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

public sealed partial class ComparePanelViewModel(PhotoTileViewModel tile) : ObservableObject
{
    public PhotoTileViewModel Tile { get; } = tile;

    [ObservableProperty]
    private BitmapSource? _image;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _error;

    /// <summary>The panel that receives decision keys (keyboard focus within compare).</summary>
    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// Side-by-side comparison of 2–4 photos. Images are decoded at display size, not full resolution,
/// so four panels stay well within memory limits.
/// </summary>
public sealed partial class CompareViewModel(IPhotoBitmapLoader loader, ILogger<CompareViewModel> logger) : ObservableObject, IDisposable
{
    public const int MinPhotos = 2;
    public const int MaxPhotos = 4;
    public const int DisplayPixelSize = 2048;

    private readonly SemaphoreSlim _decodeGate = new(2, 2);
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private IReadOnlyList<ComparePanelViewModel> _panels = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(PreviousCommand))]
    private ComparePanelViewModel? _active;

    public event EventHandler? CloseRequested;

    /// <summary>All panel loads; exposed so tests can await completion.</summary>
    internal Task CurrentLoad { get; private set; } = Task.CompletedTask;

    public void Open(IReadOnlyList<PhotoTileViewModel> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count < MinPhotos)
        {
            throw new ArgumentException($"Compare needs at least {MinPhotos} photos.", nameof(tiles));
        }

        Close();
        Panels = tiles.Take(MaxPhotos).Select(t => new ComparePanelViewModel(t)).ToList();
        Active = Panels[0];

        var cts = new CancellationTokenSource();
        _cts = cts;
        CurrentLoad = Task.WhenAll(Panels.Select(p => LoadAsync(p, cts.Token)));
    }

    public void Close()
    {
        _cts?.Cancel();
        _cts = null;
        foreach (var panel in Panels)
        {
            panel.Image = null;
        }

        Panels = [];
        Active = null;
    }

    public void Dispose()
    {
        Close();
        _decodeGate.Dispose();
    }

    private bool CanGoNext() => Active is not null && IndexOfActive() < Panels.Count - 1;

    private bool CanGoPrevious() => Active is not null && IndexOfActive() > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Active = Panels[IndexOfActive() + 1];

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Active = Panels[IndexOfActive() - 1];

    [RelayCommand]
    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Activate(ComparePanelViewModel? panel)
    {
        if (panel is not null && Panels.Contains(panel))
        {
            Active = panel;
        }
    }

    partial void OnActiveChanged(ComparePanelViewModel? oldValue, ComparePanelViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    private int IndexOfActive()
    {
        for (var i = 0; i < Panels.Count; i++)
        {
            if (ReferenceEquals(Panels[i], Active))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task LoadAsync(ComparePanelViewModel panel, CancellationToken token)
    {
        var acquired = false;
        try
        {
            await _decodeGate.WaitAsync(token).ConfigureAwait(true);
            acquired = true;
            var path = panel.Tile.FilePath;
            var bitmap = await Task.Run(() => loader.LoadForDisplay(path, DisplayPixelSize, token), token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
            {
                panel.Image = bitmap;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Compare closed.
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogLoadFailed(ex, panel.Tile.FilePath);
            panel.Error = "File not found";
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogLoadFailed(ex, panel.Tile.FilePath);
            panel.Error = "Can't display this photo";
        }
        finally
        {
            if (acquired)
            {
                _decodeGate.Release();
            }

            panel.IsLoading = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Compare could not load {FilePath}")]
    private partial void LogLoadFailed(Exception exception, string filePath);
}
