using System.IO;
using System.Windows.Media.Imaging;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.Services;

/// <summary>A request for one photo's thumbnail. Callbacks are invoked on the UI thread.</summary>
public interface IThumbnailRequester
{
    Photo Photo { get; }

    /// <summary>The edit to show. Read on a worker thread; recipes are immutable.</summary>
    EditRecipe Recipe { get; }

    void OnThumbnailLoaded();

    void OnThumbnailFailed(string message);

    void OnThumbnailRequestDropped();
}

public interface IThumbnailSource
{
    /// <summary>
    /// UI thread only. Returns the thumbnail in memory, or null. <paramref name="isCurrent"/> is false when it was
    /// rendered for a different recipe: show it until the up-to-date one arrives, and request a reload.
    /// </summary>
    BitmapSource? TryGetCached(Guid photoId, EditRecipe recipe, out bool isCurrent);

    /// <summary>UI thread only. Queues a load; the most recent requests are served first.</summary>
    void Request(IThumbnailRequester requester);
}

/// <summary>
/// Loads thumbnails for visible grid tiles with bounded concurrency and bounded memory.
/// Requests are served newest-first so tiles the user just scrolled to load before ones
/// that scrolled away; the oldest pending requests are dropped beyond <see cref="MaxPending"/>.
/// </summary>
internal sealed partial class ThumbnailLoader : IThumbnailSource, IDisposable
{
    public const int MemoryCapacity = 400;
    public const int MaxPending = 256;
    private const int WorkerCount = 3;

    private readonly IThumbnailService _thumbnails;
    private readonly IPhotoBitmapLoader _bitmaps;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ThumbnailLoader> _logger;
    private readonly LruCache<Guid, (BitmapSource Bitmap, EditRecipe Recipe)> _memory = new(MemoryCapacity);
    private readonly Lock _gate = new();
    private readonly LinkedList<IThumbnailRequester> _pending = new();
    private readonly Dictionary<Guid, LinkedListNode<IThumbnailRequester>> _pendingById = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private int _disposed;

    public ThumbnailLoader(IThumbnailService thumbnails, IPhotoBitmapLoader bitmaps, IUiDispatcher dispatcher, ILogger<ThumbnailLoader> logger)
    {
        _thumbnails = thumbnails;
        _bitmaps = bitmaps;
        _dispatcher = dispatcher;
        _logger = logger;
        _workers = Enumerable.Range(0, WorkerCount).Select(_ => Task.Run(WorkerLoopAsync)).ToArray();
    }

    public BitmapSource? TryGetCached(Guid photoId, EditRecipe recipe, out bool isCurrent)
    {
        if (_memory.TryGet(photoId, out var entry))
        {
            isCurrent = Equals(entry.Recipe, recipe);
            return entry.Bitmap;
        }

        isCurrent = false;
        return null;
    }

    public void Request(IThumbnailRequester requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        IThumbnailRequester? dropped = null;

        lock (_gate)
        {
            var id = requester.Photo.Id;
            if (_pendingById.TryGetValue(id, out var existing))
            {
                // Re-requested (scrolled back into view): move to the front.
                _pending.Remove(existing);
                _pending.AddFirst(existing);
                return;
            }

            _pendingById[id] = _pending.AddFirst(requester);
            if (_pending.Count > MaxPending)
            {
                var oldest = _pending.Last!;
                _pending.RemoveLast();
                _pendingById.Remove(oldest.Value.Photo.Id);
                dropped = oldest.Value;
            }
        }

        dropped?.OnThumbnailRequestDropped();
        _signal.Release();
    }

    /// <remarks>
    /// Idempotent: the container disposes this instance once per registration
    /// (as itself and as <see cref="IThumbnailSource"/>).
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Workers only end by cancellation; any other fault was already logged in the loop.
        }

        _shutdown.Dispose();
        _signal.Dispose();
    }

    private async Task WorkerLoopAsync()
    {
        var token = _shutdown.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            IThumbnailRequester? requester;
            lock (_gate)
            {
                var node = _pending.First;
                if (node is null)
                {
                    continue; // the request was dropped after it was signalled
                }

                _pending.RemoveFirst();
                _pendingById.Remove(node.Value.Photo.Id);
                requester = node.Value;
            }

            await LoadAsync(requester, token).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(IThumbnailRequester requester, CancellationToken token)
    {
        var photo = requester.Photo;
        try
        {
            var path = await _thumbnails.GetOrCreateAsync(photo, token).ConfigureAwait(false);
            var bitmap = _bitmaps.LoadThumbnail(path);
            var recipe = requester.Recipe;
            if (!recipe.IsNeutral)
            {
                // Edited thumbnails are rendered from the cached thumbnail with the same renderer as export.
                bitmap = BitmapConversions.ToBitmap(Rendering.EditRenderer.Render(BitmapConversions.ToBuffer(bitmap), recipe, null, token));
            }

            _dispatcher.Post(() =>
            {
                _memory.Set(photo.Id, (bitmap, recipe));
                requester.OnThumbnailLoaded();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogThumbnailFailed(ex, photo.FilePath);
            _dispatcher.Post(() => requester.OnThumbnailFailed("File not found"));
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            LogThumbnailFailed(ex, photo.FilePath);
            _dispatcher.Post(() => requester.OnThumbnailFailed("Can't display"));
        }
#pragma warning disable CA1031 // A long-lived worker must survive unexpected errors; they are logged at Error level.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogThumbnailUnexpected(ex, photo.FilePath);
            _dispatcher.Post(() => requester.OnThumbnailFailed("Can't display"));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Thumbnail could not be created for {FilePath}")]
    private partial void LogThumbnailFailed(Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error creating thumbnail for {FilePath}")]
    private partial void LogThumbnailUnexpected(Exception exception, string filePath);
}
