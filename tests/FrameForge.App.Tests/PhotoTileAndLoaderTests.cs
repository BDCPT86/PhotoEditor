using System.IO;
using FrameForge.App.Services;
using FrameForge.App.ViewModels;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

public sealed class PhotoTileAndLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Tile_RequestsThumbnailOnceOnCacheMiss()
    {
        var source = new FakeThumbnailSource();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), source);

        Assert.Null(tile.Thumbnail);
        Assert.Null(tile.Thumbnail);

        Assert.Single(source.Requests);
    }

    [Fact]
    public void Tile_ReturnsCachedThumbnail_WithoutRequesting()
    {
        var source = new FakeThumbnailSource();
        var photo = TestPhotos.Create("a.jpg");
        source.Cached[photo.Id] = TestPhotos.Bitmap();
        var tile = new PhotoTileViewModel(photo, source);

        Assert.NotNull(tile.Thumbnail);
        Assert.Empty(source.Requests);
    }

    [Theory]
    [InlineData(ImportStatus.Missing, "Missing")]
    [InlineData(ImportStatus.Failed, "Unreadable")]
    public void Tile_ForProblemPhoto_ShowsStatus_AndNeverRequests(ImportStatus status, string expected)
    {
        var source = new FakeThumbnailSource();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg", status), source);

        Assert.Null(tile.Thumbnail);
        Assert.Empty(source.Requests);
        Assert.Equal(expected, tile.StatusText);
        Assert.True(tile.HasProblem);
    }

    [Fact]
    public void Tile_AfterThumbnailFailure_ShowsErrorAndStopsRetrying()
    {
        var source = new FakeThumbnailSource();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), source);
        _ = tile.Thumbnail;

        ((IThumbnailRequester)tile).OnThumbnailFailed("Can't display");
        _ = tile.Thumbnail;

        Assert.Equal("Can't display", tile.StatusText);
        Assert.Single(source.Requests);
    }

    [Fact]
    public void Tile_DroppedRequest_IsRequestedAgainWhenShownAgain()
    {
        var source = new FakeThumbnailSource();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), source);
        _ = tile.Thumbnail;

        ((IThumbnailRequester)tile).OnThumbnailRequestDropped();
        _ = tile.Thumbnail;

        Assert.Equal(2, source.Requests.Count);
    }

    [Fact]
    public void DetailsText_SummarisesAvailableMetadataOnly()
    {
        var photo = TestPhotos.Create("a.jpg");
        photo.Width = 6000;
        photo.Height = 4000;
        photo.CameraModel = "Model X";
        photo.Iso = 400;
        photo.FNumber = 2.8;

        var details = PhotoTileViewModel.BuildDetails(photo);

        Assert.Contains("Model X", details, StringComparison.Ordinal);
        Assert.Contains("ISO 400", details, StringComparison.Ordinal);
        Assert.DoesNotContain("mm", details, StringComparison.Ordinal);
        Assert.Equal("No metadata", PhotoTileViewModel.BuildDetails(TestPhotos.Create("b.jpg")));
    }

    [Fact]
    public void DetailsText_ShowsDisplayedDimensions_ForRotatedPhotos()
    {
        var photo = TestPhotos.Create("rotated.jpg");
        photo.Width = 2400;
        photo.Height = 1600;
        photo.Orientation = 6;

        Assert.StartsWith("1600 × 2400", PhotoTileViewModel.BuildDetails(photo), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loader_LoadsIntoMemoryCache_AndNotifiesRequester()
    {
        using var loader = new ThumbnailLoader(
            new FakeThumbnailService(_ => "thumb.jpg"),
            new ScriptedBitmapLoader(),
            new ImmediateDispatcher(),
            NullLogger<ThumbnailLoader>.Instance);
        var requester = new AwaitableRequester(TestPhotos.Create("a.jpg"));

        loader.Request(requester);
        var error = await requester.Completion.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Null(error);
        Assert.NotNull(loader.TryGetCached(requester.Photo.Id, FrameForge.Core.Editing.EditRecipe.Neutral, out _));
    }

    [Fact]
    public async Task Loader_ReportsMissingSourceAsFileNotFound()
    {
        using var loader = new ThumbnailLoader(
            new FakeThumbnailService(p => throw new FileNotFoundException("gone", p.FilePath)),
            new ScriptedBitmapLoader(),
            new ImmediateDispatcher(),
            NullLogger<ThumbnailLoader>.Instance);
        var requester = new AwaitableRequester(TestPhotos.Create("a.jpg"));

        loader.Request(requester);

        Assert.Equal("File not found", await requester.Completion.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        Assert.Null(loader.TryGetCached(requester.Photo.Id, FrameForge.Core.Editing.EditRecipe.Neutral, out _));
    }

    [Fact]
    public async Task Loader_DropsOldestPendingRequests_BeyondTheLimit()
    {
        using var gate = new ManualResetEventSlim(false);
        using var loader = new ThumbnailLoader(
            new FakeThumbnailService(_ =>
            {
                gate.Wait(TimeSpan.FromSeconds(10)); // keep workers busy so requests stay pending
                return "thumb.jpg";
            }),
            new ScriptedBitmapLoader(),
            new ImmediateDispatcher(),
            NullLogger<ThumbnailLoader>.Instance);

        var requesters = Enumerable.Range(0, ThumbnailLoader.MaxPending + 20)
            .Select(i => new AwaitableRequester(TestPhotos.Create($"{i}.jpg")))
            .ToList();
        foreach (var r in requesters)
        {
            loader.Request(r);
        }

        gate.Set();
        await Task.WhenAll(requesters.Where(r => !r.Dropped).Select(r => r.Completion)).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.InRange(requesters.Count(r => r.Dropped), 1, 20);
        Assert.False(requesters[^1].Dropped); // newest requests are kept
    }

    [Fact]
    public void Loader_CanBeDisposedTwice()
    {
        // The DI container disposes it once per registration (concrete type and IThumbnailSource).
        var loader = new ThumbnailLoader(
            new FakeThumbnailService(_ => "thumb.jpg"),
            new ScriptedBitmapLoader(),
            new ImmediateDispatcher(),
            NullLogger<ThumbnailLoader>.Instance);

        loader.Dispose();
        var second = Record.Exception(loader.Dispose);

        Assert.Null(second);
    }

    [Fact]
    public void LibrarySummary_ExplainsCancellationAndProblems()
    {
        var text = LibraryViewModel.DescribeSummary(new ImportSummary
        {
            FolderPath = @"C:\Photos\Wedding",
            Added = 10,
            Failed = 2,
            MarkedMissing = 1,
            SkippedFolders = [@"C:\Photos\Wedding\Locked"],
            WasCancelled = true,
        });

        Assert.Contains("Wedding", text, StringComparison.Ordinal);
        Assert.Contains("cancelled", text, StringComparison.Ordinal);
        Assert.Contains("2 unreadable", text, StringComparison.Ordinal);
        Assert.Contains("1 missing", text, StringComparison.Ordinal);
        Assert.Contains("could not be read", text, StringComparison.Ordinal);
        Assert.Contains("continue", text, StringComparison.Ordinal);
    }
}
