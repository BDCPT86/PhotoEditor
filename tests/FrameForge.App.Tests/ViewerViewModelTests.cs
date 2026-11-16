using FrameForge.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

public sealed class ViewerViewModelTests : IDisposable
{
    private readonly ScriptedBitmapLoader _loader = new();
    private readonly FakeThumbnailSource _thumbnails = new();
    private readonly ViewerViewModel _viewer;
    private readonly List<PhotoTileViewModel> _tiles;

    public ViewerViewModelTests()
    {
        _viewer = new ViewerViewModel(_loader, NullLogger<ViewerViewModel>.Instance);
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
    public async Task Close_ReleasesTheFullResolutionImage()
    {
        _viewer.Open(_tiles, 2);
        await _viewer.CurrentLoad;

        _viewer.Close();

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
