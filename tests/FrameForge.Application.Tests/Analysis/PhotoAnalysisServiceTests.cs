using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Analysis;

public sealed class PhotoAnalysisServiceTests
{
    private readonly MemoryAnalysisRepository _repository = new();
    private readonly FakeImageLoader _loader = new();
    private readonly FakeHasher _hasher = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PhotoAnalysisService CreateService(int parallelism = 4, int batch = 10) => new(
        _repository, _loader, _hasher,
        new PhotoAnalysisOptions { MaxParallelism = parallelism, BatchSize = batch, ProgressInterval = TimeSpan.Zero },
        TimeProvider.System, NullLogger<PhotoAnalysisService>.Instance);

    private static List<Photo> Photos(int count) =>
        Enumerable.Range(0, count).Select(i => AnalysisTestPhotos.Create($"IMG_{i:000}.jpg")).ToList();

    [Fact]
    public async Task FirstRun_AnalysesEverything_SecondRunUsesTheCache()
    {
        var photos = Photos(12);
        var service = CreateService();

        var first = await service.AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);
        var loadsAfterFirst = _loader.Loads;
        var second = await service.AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);

        Assert.Equal(12, first.Analysed);
        Assert.Equal(12, second.Cached);
        Assert.Equal(0, second.Analysed);
        Assert.Equal(loadsAfterFirst, _loader.Loads); // nothing decoded again
        Assert.Equal(12 * AnalysisCache.AllKinds.Count, _repository.Rows.Count);

        var loaded = await service.LoadAsync(photos, AnalysisSettings.Default, Ct);
        Assert.All(photos, p => Assert.True(loaded[p.Id].IsComplete));
    }

    [Fact]
    public async Task ChangingAComputationSetting_RecomputesImageMetrics_ButNotTheFileHash()
    {
        var photos = Photos(5);
        var service = CreateService();
        await service.AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);
        var hashesBefore = _hasher.Calls;
        var loadsBefore = _loader.Loads;

        var larger = AnalysisSettings.Default with { MaxPixelSize = 2048 };
        var stale = await service.LoadAsync(photos, larger, Ct);
        Assert.All(stale.Values, a =>
        {
            Assert.Null(a.Sharpness);        // computed at the old resolution: stale
            Assert.NotNull(a.ContentHash);   // unaffected by resolution: still valid
        });

        var summary = await service.AnalyzeAsync(photos, larger, null, Ct);

        Assert.Equal(5, summary.Analysed);
        Assert.Equal(loadsBefore + 5, _loader.Loads);
        Assert.Equal(hashesBefore, _hasher.Calls); // content hash has no settings, stays cached
        Assert.All(_repository.Rows.Where(r => r.Kind == AnalysisKind.Sharpness), r => Assert.Equal(larger.SharpnessSettingsHash, r.SettingsHash));
    }

    [Fact]
    public async Task ChangedAlgorithmVersion_InvalidatesOnlyThatKind()
    {
        var photo = AnalysisTestPhotos.Create("a.jpg");
        var service = CreateService();
        await service.AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);
        var exposure = _repository.Rows.Single(r => r.Kind == AnalysisKind.Exposure);
        _repository.Replace(exposure with { AlgorithmVersion = "old-version" });

        var loaded = await service.LoadAsync([photo], AnalysisSettings.Default, Ct);
        Assert.Null(loaded[photo.Id].Exposure);
        Assert.NotNull(loaded[photo.Id].Sharpness);

        var loadsBefore = _loader.Loads;
        await service.AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);
        Assert.Equal(loadsBefore + 1, _loader.Loads);
        Assert.Equal(ExposureAnalyzer.Version, _repository.Rows.Single(r => r.Kind == AnalysisKind.Exposure).AlgorithmVersion);
    }

    [Fact]
    public async Task ModifiedSourceFile_InvalidatesEverything()
    {
        var photo = AnalysisTestPhotos.Create("a.jpg");
        var service = CreateService();
        await service.AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);

        photo.LastModifiedUtc = photo.LastModifiedUtc.AddMinutes(5); // as after a re-import of an edited file

        Assert.Empty(await service.LoadAsync([photo], AnalysisSettings.Default, Ct));
        var summary = await service.AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);
        Assert.Equal(1, summary.Analysed);
        Assert.Equal(2, _hasher.Calls);
    }

    [Fact]
    public async Task MissingAndUnreadablePhotos_AreNotAttempted()
    {
        List<Photo> photos = [AnalysisTestPhotos.Create("ok.jpg"), AnalysisTestPhotos.Create("gone.jpg", ImportStatus.Missing), AnalysisTestPhotos.Create("bad.jpg", ImportStatus.Failed)];

        var summary = await CreateService().AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);

        Assert.Equal(1, summary.Analysed);
        Assert.Equal(2, summary.NotEligible);
        Assert.Equal(1, _loader.Loads);
    }

    [Fact]
    public async Task DecodeFailure_IsCounted_AndOthersAreStillAnalysed()
    {
        var photos = Photos(6);
        _loader.Corrupt.Add(photos[2].FilePath);

        var summary = await CreateService().AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);

        Assert.Equal(5, summary.Analysed);
        Assert.Equal(1, summary.Failed);
        Assert.DoesNotContain(_repository.Rows, r => r.PhotoId == photos[2].Id);
    }

    [Fact]
    public async Task Cancellation_KeepsFinishedPhotos_AndTheNextRunResumes()
    {
        var photos = Photos(30);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _loader.AfterLoad = n =>
        {
            if (n == 8)
            {
                cts.Cancel();
            }
        };
        var service = CreateService(parallelism: 1, batch: 5);

        var cancelled = await service.AnalyzeAsync(photos, AnalysisSettings.Default, null, cts.Token);

        Assert.True(cancelled.WasCancelled);
        var done = _repository.Rows.Select(r => r.PhotoId).Distinct().Count();
        Assert.InRange(done, 8, 29);
        Assert.Equal(cancelled.Analysed, done);

        _loader.AfterLoad = null;
        var resumed = await service.AnalyzeAsync(photos, AnalysisSettings.Default, null, Ct);
        Assert.Equal(done, resumed.Cached);
        Assert.Equal(30 - done, resumed.Analysed);
    }

    [Fact]
    public async Task Concurrency_IsBounded()
    {
        _loader.Delay = TimeSpan.FromMilliseconds(15);

        await CreateService(parallelism: 2, batch: 20).AnalyzeAsync(Photos(20), AnalysisSettings.Default, null, Ct);

        Assert.InRange(_loader.MaxConcurrent, 1, 2);
    }

    [Fact]
    public async Task EachPhotoIsDecodedOnce_ForAllImageMetrics()
    {
        await CreateService().AnalyzeAsync(Photos(7), AnalysisSettings.Default, null, Ct);
        Assert.Equal(7, _loader.Loads);
    }
}
