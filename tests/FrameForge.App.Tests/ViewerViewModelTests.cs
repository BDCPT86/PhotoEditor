using FrameForge.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

public sealed class ViewerViewModelTests : IDisposable
{
    private readonly ScriptedBitmapLoader _loader = new();
    private readonly EditStack _edit = new();
    private readonly FakeThumbnailSource _thumbnails = new();
    private readonly ViewerViewModel _viewer;
    private readonly List<PhotoTileViewModel> _tiles;

    public ViewerViewModelTests()
    {
        _viewer = new ViewerViewModel(_loader, _edit.Renderer, _edit.Develop, _edit.Faces, NullLogger<ViewerViewModel>.Instance);
        _tiles = Enumerable.Range(1, 4)
            .Select(i => new PhotoTileViewModel(TestPhotos.Create($"IMG_{i}.jpg"), _thumbnails))
            .ToList();
    }

    public void Dispose() => _viewer.Dispose();

    [Fact]
    public async Task Open_LoadsTheChosenPhoto()
    {
        _viewer.Open(_tiles, 1);
        await _viewer.CurrentLoad;

        Assert.Same(_tiles[1], _viewer.Current);
        Assert.NotNull(_viewer.Image);
        Assert.False(_viewer.IsLoading);
        Assert.Null(_viewer.ErrorMessage);
        Assert.EndsWith("2 / 4", _viewer.PositionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Navigation_StopsAtBothEnds()
    {
        _viewer.Open(_tiles, 0);
        await _viewer.CurrentLoad;

        Assert.False(_viewer.PreviousCommand.CanExecute(null));
        Assert.False(_viewer.FirstCommand.CanExecute(null));
        Assert.True(_viewer.NextCommand.CanExecute(null));

        _viewer.LastCommand.Execute(null);
        await _viewer.CurrentLoad;

        Assert.Same(_tiles[3], _viewer.Current);
        Assert.False(_viewer.NextCommand.CanExecute(null));
        Assert.True(_viewer.PreviousCommand.CanExecute(null));

        _viewer.PreviousCommand.Execute(null);
        await _viewer.CurrentLoad;
        Assert.Same(_tiles[2], _viewer.Current);
    }

    [Fact]
    public async Task MissingFile_ShowsErrorInsteadOfImage()
    {
        _loader.Behaviours[_tiles[0].FilePath] = FileErrors.NotFound(_tiles[0].FilePath);

        _viewer.Open(_tiles, 0);
        await _viewer.CurrentLoad;

        Assert.Null(_viewer.Image);
        Assert.Contains("no longer exists", _viewer.ErrorMessage, StringComparison.Ordinal);
        Assert.False(_viewer.IsLoading);
    }

    [Fact]
    public async Task CorruptFile_ShowsErrorAndNextPhotoStillLoads()
    {
        _loader.Behaviours[_tiles[0].FilePath] = FileErrors.Corrupt();

        _viewer.Open(_tiles, 0);
        await _viewer.CurrentLoad;
        Assert.Contains("can't be displayed", _viewer.ErrorMessage, StringComparison.Ordinal);

        _viewer.NextCommand.Execute(null);
        await _viewer.CurrentLoad;

        Assert.Null(_viewer.ErrorMessage);
        Assert.NotNull(_viewer.Image);
    }

    [Fact]
    public async Task MovingOn_CancelsTheSlowLoad_AndShowsTheNewPhoto()
    {
        using var release = new ManualResetEventSlim(false);
        var slowStarted = new TaskCompletionSource();
        var fast = TestPhotos.Bitmap(8, 8);
        _loader.Behaviours[_tiles[0].FilePath] = token =>
        {
            slowStarted.TrySetResult();
            WaitHandle.WaitAny([release.WaitHandle, token.WaitHandle]);
            token.ThrowIfCancellationRequested();
            return TestPhotos.Bitmap();
        };
        _loader.Behaviours[_tiles[1].FilePath] = _ => fast;

        _viewer.Open(_tiles, 0);
        var slowLoad = _viewer.CurrentLoad;
        await slowStarted.Task;

        _viewer.NextCommand.Execute(null);
        await _viewer.CurrentLoad;
        await slowLoad;

        Assert.Same(_tiles[1], _viewer.Current);
        Assert.Same(fast, _viewer.Image);
        Assert.False(_viewer.IsLoading);
    }

    [Fact]
    public async Task Photos_AreLoadedAtScreenSize_NotFullResolution()
    {
        _viewer.DisplayLongSide = 1536;
        _viewer.Open(_tiles, 1);
        await _viewer.CurrentLoad;

        Assert.True(_viewer.Image is not null, $"No image: error={_viewer.ErrorMessage} loading={_viewer.IsLoading} current={_viewer.Current?.FileName} loads={_loader.CompletedLoads}");
        Assert.Equal(0, _loader.FullLoadsRequested);
        lock (_loader.DisplaySizesRequested)
        {
            Assert.All(_loader.DisplaySizesRequested, size => Assert.Equal(1536, size));
        }
    }

    [Fact]
    public async Task TheNextPhoto_IsLoadedAhead_AndShowsWithoutWaiting()
    {
        var next = TestPhotos.Bitmap(6, 4);
        _loader.Behaviours[_tiles[2].FilePath] = _ => next;
        _viewer.Open(_tiles, 1);
        await _viewer.CurrentLoad;
        await WaitUntil(() => _loader.CompletedLoads >= 3); // the photo, the next one, the one behind

        _viewer.NextCommand.Execute(null);

        Assert.Same(next, _viewer.Image); // already there: no await needed
        Assert.False(_viewer.IsLoading);
    }

    [Fact]
    public async Task ASlowLookAhead_DoesNotDelayThePhotoShown()
    {
        using var release = new ManualResetEventSlim(false);
        _loader.Behaviours[_tiles[1].FilePath] = token =>
        {
            WaitHandle.WaitAny([release.WaitHandle, token.WaitHandle]); // the next photo is slow to read
            token.ThrowIfCancellationRequested();
            return TestPhotos.Bitmap();
        };

        _viewer.Open(_tiles, 0); // looks ahead at the second photo, which hangs
        await _viewer.CurrentLoad;
        _viewer.NextCommand.Execute(null); // the hanging photo: still wanted (it is now the one behind)
        _viewer.NextCommand.Execute(null);
        await _viewer.CurrentLoad.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Same(_tiles[2], _viewer.Current);
        Assert.NotNull(_viewer.Image);
        release.Set();
    }

    [Fact]
    public async Task ZoomingIn_LoadsTheFullResolutionImage_ForThisPhotoOnly()
    {
        _tiles[0].Photo.Width = 6000;
        _tiles[0].Photo.Height = 4000;
        var full = TestPhotos.Bitmap(60, 40);
        _viewer.Open(_tiles, 0);
        await _viewer.CurrentLoad;
        Assert.Equal(6000, _viewer.DisplayFullWidth); // zoom refers to the photo's real size
        _loader.Behaviours[_tiles[0].FilePath] = _ => full;

        _viewer.WantsDetail = true;
        await WaitUntil(() => _viewer.Image == full);

        Assert.Equal(1, _loader.FullLoadsRequested);
        Assert.Equal(_tiles[0].Id, _viewer.DisplayKey); // same key: the image area keeps the zoom

        _viewer.WantsDetail = false;
        _viewer.NextCommand.Execute(null);
        await _viewer.CurrentLoad;
        Assert.NotSame(full, _viewer.Image);
        Assert.Equal(1, _loader.FullLoadsRequested);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task Close_ReleasesTheFullResolutionImage()
    {
        _viewer.Open(_tiles, 2);
        await _viewer.CurrentLoad;

        await _viewer.CloseAsync();

        Assert.Null(_viewer.Image);
    }

    [Fact]
    public void RequestClose_RaisesEvent()
    {
        var raised = false;
        _viewer.CloseRequested += (_, _) => raised = true;

        _viewer.RequestCloseCommand.Execute(null);

        Assert.True(raised);
    }
}
