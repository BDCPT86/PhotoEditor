using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.App.Services;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Imaging;

namespace FrameForge.App.Tests;

internal static class TestPhotos
{
    public static Photo Create(string name, ImportStatus status = ImportStatus.Imported) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = $@"C:\Shoot\{name}",
        FileName = name,
        FolderPath = @"C:\Shoot",
        Status = status,
    };

    public static BitmapSource Bitmap(int width = 4, int height = 3)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, new byte[width * height], width);
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed class FakeThumbnailSource : IThumbnailSource
{
    public Dictionary<Guid, BitmapSource> Cached { get; } = [];

    public List<IThumbnailRequester> Requests { get; } = [];

    public BitmapSource? TryGetCached(Guid photoId) => Cached.GetValueOrDefault(photoId);

    public void Request(IThumbnailRequester requester) => Requests.Add(requester);
}

/// <summary>Bitmap loader whose behaviour is scripted per file path.</summary>
internal sealed class ScriptedBitmapLoader : IPhotoBitmapLoader
{
    private int _completedLoads;

    public Dictionary<string, Func<CancellationToken, BitmapSource>> Behaviours { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CompletedLoads => Volatile.Read(ref _completedLoads);

    public BitmapSource LoadThumbnail(string thumbnailPath) => TestPhotos.Bitmap();

    public List<int> DisplaySizesRequested { get; } = [];

    public BitmapSource LoadFull(string photoPath, CancellationToken cancellationToken)
    {
        var result = Behaviours.TryGetValue(photoPath, out var behaviour) ? behaviour(cancellationToken) : TestPhotos.Bitmap();
        Interlocked.Increment(ref _completedLoads);
        return result;
    }

    public BitmapSource LoadForDisplay(string photoPath, int maxPixelSize, CancellationToken cancellationToken)
    {
        lock (DisplaySizesRequested)
        {
            DisplaySizesRequested.Add(maxPixelSize);
        }

        return LoadFull(photoPath, cancellationToken);
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeThumbnailService(Func<Photo, string> behaviour) : IThumbnailService
{
    public Task<string> GetOrCreateAsync(Photo photo, CancellationToken cancellationToken = default) =>
        Task.Run(() => behaviour(photo), cancellationToken);
}

internal sealed class AwaitableRequester(Photo photo) : IThumbnailRequester
{
    private readonly TaskCompletionSource<string?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Photo Photo { get; } = photo;

    /// <summary>Completes with null on success, or the failure message.</summary>
    public Task<string?> Completion => _done.Task;

    public bool Dropped { get; private set; }

    public void OnThumbnailLoaded() => _done.TrySetResult(null);

    public void OnThumbnailFailed(string message) => _done.TrySetResult(message);

    public void OnThumbnailRequestDropped() => Dropped = true;
}

internal static class FileErrors
{
    public static Func<CancellationToken, BitmapSource> NotFound(string path) =>
        _ => throw new FileNotFoundException("gone", path);

    public static Func<CancellationToken, BitmapSource> Corrupt() =>
        _ => throw new PhotoReadException("corrupt");
}
