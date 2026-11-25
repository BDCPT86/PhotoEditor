using System.Windows.Media.Imaging;
using FrameForge.App.ViewModels;
using FrameForge.Core.Editing;

namespace FrameForge.App.Services;

/// <summary>
/// Screen-size images for the viewer: the photo shown and its neighbours, loaded ahead so stepping through photos
/// doesn't wait for a decode. Memory is bounded: at most <see cref="Capacity"/> finished images (about 17 MB each at
/// 2560 px) plus the loads in progress, which are only for wanted photos (see <see cref="Want"/>).
/// <list type="bullet">
/// <item>The photo being shown (<see cref="GetAsync"/>) loads straight away, never behind a look-ahead.</item>
/// <item>Look-aheads load one at a time. A queued look-ahead for the photo now wanted is replaced by a direct load.</item>
/// </list>
/// Keyed by photo, recipe and size, so an edit or a different window size loads afresh.
/// </summary>
public sealed class ViewerImageCache(Func<PhotoTileViewModel, int, CancellationToken, Task<BitmapSource>> load)
{
    public const int Capacity = 4;

    private readonly Lock _sync = new();
    private readonly LruCache<Key, BitmapSource> _ready = new(Capacity);
    private readonly Dictionary<Key, Pending> _pending = [];
    private Task _lookAheads = Task.CompletedTask;

    private sealed record Key(Guid PhotoId, EditRecipe Recipe, int LongSide);

    private sealed class Pending(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;

        public Task<BitmapSource> Task { get; set; } = null!;

        /// <summary>Set when the decode has begun (a look-ahead may still be queued before that).</summary>
        public volatile bool Running;
    }

    private static Key KeyOf(PhotoTileViewModel tile, int longSide) => new(tile.Id, tile.Recipe, longSide);

    /// <summary>The image if it is ready now.</summary>
    public BitmapSource? TryGet(PhotoTileViewModel tile, int longSide)
    {
        ArgumentNullException.ThrowIfNull(tile);
        lock (_sync)
        {
            return _ready.TryGet(KeyOf(tile, longSide), out var image) ? image : null;
        }
    }

    /// <summary>
    /// The image, from the cache, from a load already running, or from a new load that starts at once.
    /// <paramref name="cancellationToken"/> stops waiting; the load itself continues while the photo stays wanted.
    /// </summary>
    public Task<BitmapSource> GetAsync(PhotoTileViewModel tile, int longSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var key = KeyOf(tile, longSide);
        lock (_sync)
        {
            if (_ready.TryGet(key, out var image))
            {
                return Task.FromResult(image);
            }

            if (_pending.TryGetValue(key, out var pending))
            {
                if (pending.Running)
                {
                    return pending.Task.WaitAsync(cancellationToken);
                }

                // Still queued behind other look-aheads: load it now instead.
                pending.Cts.Cancel();
                _pending.Remove(key);
            }

            return Start(key, tile, after: Task.CompletedTask).Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Keeps only <paramref name="wanted"/>: loads for other photos are cancelled. Those of <paramref name="wanted"/>
    /// that aren't ready or loading are queued as look-aheads, in order.
    /// </summary>
    public void Want(IEnumerable<PhotoTileViewModel> wanted, int longSide)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        var tiles = wanted.ToList();
        var keys = tiles.Select(t => KeyOf(t, longSide)).ToHashSet();
        lock (_sync)
        {
            foreach (var (key, pending) in _pending.Where(p => !keys.Contains(p.Key)).ToList())
            {
                pending.Cts.Cancel();
                _pending.Remove(key);
            }

            foreach (var tile in tiles)
            {
                var key = KeyOf(tile, longSide);
                if (!_ready.TryGet(key, out _) && !_pending.ContainsKey(key))
                {
                    var pending = Start(key, tile, after: _lookAheads);
                    _lookAheads = pending.Task.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }
            }
        }
    }

    /// <summary>Cancels every load and drops every image (the viewer closed).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var pending in _pending.Values)
            {
                pending.Cts.Cancel();
            }

            _pending.Clear();
            _ready.Clear();
        }
    }

    /// <summary>Called with <see cref="_sync"/> held. <paramref name="after"/> never faults.</summary>
    private Pending Start(Key key, PhotoTileViewModel tile, Task after)
    {
        var pending = new Pending(new CancellationTokenSource());
        _pending[key] = pending;
        pending.Task = RunAsync(key, tile, pending, after);

        // A look-ahead nobody waits for may fail (an unreadable file); the viewer reports it when the photo is shown.
        _ = pending.Task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return pending;
    }

    private async Task<BitmapSource> RunAsync(Key key, PhotoTileViewModel tile, Pending pending, Task after)
    {
        var token = pending.Cts.Token;
        try
        {
            await Task.Yield(); // never decode inside the caller's lock
            await after.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            pending.Running = true;
            var image = await load(tile, key.LongSide, token).ConfigureAwait(false);
            lock (_sync)
            {
                if (!token.IsCancellationRequested)
                {
                    _ready.Set(key, image);
                }
            }

            return image;
        }
        finally
        {
            lock (_sync)
            {
                if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
                {
                    _pending.Remove(key);
                }
            }
        }
    }
}
