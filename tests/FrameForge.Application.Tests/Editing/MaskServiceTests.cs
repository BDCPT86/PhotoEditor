using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Application.Editing.Masks;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Editing;

public sealed class MaskServiceTests
{
    private const string DefaultPath = @"C:\Shoot\a.jpg";

    private static readonly Photo Photo = Make();

    private static Photo Make(string path = DefaultPath, long size = 1234, int addSeconds = 0) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = path,
        FileName = "a.jpg",
        FolderPath = @"C:\Shoot",
        FileSizeBytes = size,
        LastModifiedUtc = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc).AddSeconds(addSeconds),
        Status = ImportStatus.Imported,
    };

    private static readonly MaskReference Subject = new() { Kind = MaskKind.Subject, Source = "fake@1" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ keys

    [Fact]
    public void Key_ChangesWithThePhotoTheSourceTheSizeAndTheClicks()
    {
        var key = MaskKey.Compute(Photo, "fake@1", [], 1024);

        Assert.Equal(key, MaskKey.Compute(Make(path: @"c:\shoot\A.JPG"), "fake@1", [], 1024));
        Assert.NotEqual(key, MaskKey.Compute(Make(addSeconds: 1), "fake@1", [], 1024));
        Assert.NotEqual(key, MaskKey.Compute(Make(size: 1235), "fake@1", [], 1024));
        Assert.NotEqual(key, MaskKey.Compute(Photo, "fake@2", [], 1024));
        Assert.NotEqual(key, MaskKey.Compute(Photo, "fake@1", [], 512));
        Assert.NotEqual(key, MaskKey.Compute(Photo, "fake@1", [new MaskPoint(0.5, 0.5, true)], 1024));
        Assert.NotEqual(MaskKey.Compute(Photo, "fake@1", [new MaskPoint(0.5, 0.5, true)], 1024), MaskKey.Compute(Photo, "fake@1", [new MaskPoint(0.5, 0.5, false)], 1024));
        Assert.Matches("^[0-9a-f]{32}$", key);
    }

    // ------------------------------------------------------------------ service

    [Fact]
    public async Task ComputedMask_IsCached_AndTheSecondRequestUsesTheCache()
    {
        var generator = new Generator();
        var cache = new Cache();
        using var service = Service(generator, cache);

        var first = await service.GetAsync(Photo, Subject, Ct);
        var second = await service.GetAsync(Photo, Subject with { Invert = true }, Ct); // Background shares the mask

        Assert.True(first.HasMask);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal("1 thing", second.Summary);
        Assert.Equal(1, generator.Calls);
        Assert.Equal("fake@1", first.Source);
    }

    [Fact]
    public async Task NotFound_IsUnavailable_AndNotCached()
    {
        var cache = new Cache();
        using var service = Service(new Generator { Finds = false }, cache);

        var outcome = await service.GetAsync(Photo, Subject, Ct);

        Assert.False(outcome.HasMask);
        Assert.Equal("Nothing here", outcome.Summary);
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task GeneratorThatIsNotReady_GivesItsReason_WithoutLoadingThePhoto()
    {
        var loader = new Loader();
        using var service = Service(new Generator { NotReady = "Install the models." }, new Cache(), loader);

        var outcome = await service.GetAsync(Photo, Subject, Ct);

        Assert.Equal("Install the models.", outcome.Summary);
        Assert.Equal(0, loader.Loads);
        Assert.Equal("Install the models.", service.UnavailableReason(MaskKind.Subject));
    }

    [Fact]
    public async Task UnsupportedKind_IsUnavailable()
    {
        using var service = Service(new Generator(), new Cache());

        var outcome = await service.GetAsync(Photo, new MaskReference { Kind = MaskKind.Sky, Source = "x" }, Ct);

        Assert.False(outcome.HasMask);
        Assert.Contains("not supported", outcome.Summary, StringComparison.Ordinal);
        Assert.Null(service.CurrentSource(MaskKind.Sky));
    }

    [Fact]
    public async Task UnreadablePhoto_IsUnavailable_NotAnException()
    {
        using var service = Service(new Generator(), new Cache(), new Loader { Fails = true });

        var outcome = await service.GetAsync(Photo, Subject, Ct);

        Assert.False(outcome.HasMask);
        Assert.StartsWith("The photo could not be read", outcome.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelFailure_IsUnavailable_WithTheReason()
    {
        using var service = Service(new Generator { Throws = true }, new Cache());

        var outcome = await service.GetAsync(Photo, Subject, Ct);

        Assert.False(outcome.HasMask);
        Assert.Equal("The subject mask could not be made: model broke", outcome.Summary);
    }

    [Fact]
    public async Task CacheWriteFailure_StillReturnsTheMask()
    {
        using var service = Service(new Generator(), new Cache { FailsToSave = true });

        Assert.True((await service.GetAsync(Photo, Subject, Ct)).HasMask);
    }

    [Fact]
    public async Task Cancellation_Propagates_AndNothingIsCached()
    {
        var cache = new Cache();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var generator = new Generator { OnGenerate = cts.Cancel };
        using var service = Service(generator, cache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(Photo, Subject, cts.Token));
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task MasksAreComputedOneAtATime()
    {
        var generator = new Generator { Delay = TimeSpan.FromMilliseconds(30) };
        using var service = Service(generator, new Cache());

        await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            service.GetAsync(Make(size: i), Subject, Ct)));

        Assert.Equal(4, generator.Calls);
        Assert.Equal(1, generator.MaxConcurrent);
    }

    [Fact]
    public async Task Clicks_ReuseTheDecodedPhoto()
    {
        var loader = new Loader();
        using var service = Service(new Generator(), new Cache(), loader);

        await service.GetAsync(Photo, Subject, Ct);
        await service.GetAsync(Photo, Subject with { Points = new([new MaskPoint(0.5, 0.5, true)]) }, Ct);
        await service.GetAsync(Make(size: 999), Subject, Ct);

        Assert.Equal(2, loader.Loads); // the second request is the same photo with a click
    }

    [Fact]
    public async Task TryGetCached_NeverComputes()
    {
        var generator = new Generator();
        using var service = Service(generator, new Cache());

        Assert.Null(service.TryGetCached(Photo, Subject));
        await service.GetAsync(Photo, Subject, Ct);
        var cached = service.TryGetCached(Photo, Subject with { Invert = true });

        Assert.NotNull(cached);
        Assert.True(cached.FromCache);
        Assert.Equal(1, generator.Calls);
    }

    private static MaskService Service(Generator generator, Cache cache, Loader? loader = null) =>
        new([generator], loader ?? new Loader(), cache, NullLogger<MaskService>.Instance);

    private sealed class Generator : IMaskGenerator
    {
        private int _active;

        public bool Finds { get; init; } = true;

        public bool Throws { get; init; }

        public string? NotReady { get; init; }

        public TimeSpan Delay { get; init; }

        public Action? OnGenerate { get; init; }

        public int Calls { get; private set; }

        public int MaxConcurrent { get; private set; }

        public MaskKind Kind => MaskKind.Subject;

        public string Source => "fake@1";

        public string? UnavailableReason => NotReady;

        public MaskGeneration Generate(AnalysisImage image, IReadOnlyList<MaskPoint> points, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            try
            {
                lock (this)
                {
                    Calls++;
                    MaxConcurrent = Math.Max(MaxConcurrent, active);
                }

                Thread.Sleep(Delay);
                OnGenerate?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (Throws)
                {
                    throw new InvalidOperationException("model broke");
                }

                return Finds
                    ? MaskGeneration.Found(new MaskImage(image.Width, image.Height, new byte[image.Width * image.Height]), "1 thing")
                    : MaskGeneration.NotFound("Nothing here");
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class Loader : IAnalysisImageLoader
    {
        public bool Fails { get; init; }

        public int Loads { get; private set; }

        public AnalysisImage Load(string path, int maxPixelSize)
        {
            Loads++;
            Assert.Equal(MaskService.AnalysisLongSide, maxPixelSize);
            return Fails ? throw new PhotoReadException("not an image") : AnalysisImage.Create(8, 6, (_, _) => (1, 2, 3));
        }
    }

    private sealed class Cache : IMaskCache
    {
        public Dictionary<string, CachedMask> Entries { get; } = [];

        public bool FailsToSave { get; init; }

        public CachedMask? TryGet(string key) => Entries.GetValueOrDefault(key);

        public void Save(string key, CachedMask mask)
        {
            if (FailsToSave)
            {
                throw new IOException("disk full");
            }

            Entries[key] = mask;
        }
    }
}
