using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Application.Tests.Analysis;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Ai;

public sealed class AiAnalysisServiceTests
{
    private static readonly AiCapabilities Both = new(true, true);
    private readonly MemoryAnalysisRepository _repository = new();
    private readonly FakeImageLoader _loader = new();
    private readonly FakeRuntime _runtime = new();

    private AiAnalysisService Service() => new(
        _repository, _loader, _runtime, new AiAnalysisOptions { MaxParallelism = 2, BatchSize = 3 }, TimeProvider.System, NullLogger<AiAnalysisService>.Instance);

    private static List<Photo> Photos(int count) => Enumerable.Range(0, count).Select(i => AnalysisTestPhotos.Create($"IMG_{i}.jpg")).ToList();

    private static Task<AnalysisRunSummary> Run(AiAnalysisService service, IReadOnlyList<Photo> photos, AiCapabilities? caps = null) =>
        service.AnalyzeAsync(photos, caps ?? Both, AnalysisSettings.Default, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Analyze_StoresFacesAndEmbeddings_ThenLoadReturnsThem()
    {
        var photos = Photos(5);
        var service = Service();

        var summary = await Run(service, photos);
        var loaded = await service.LoadAsync(photos, Both, AnalysisSettings.Default, TestContext.Current.CancellationToken);

        Assert.Equal(5, summary.Analysed);
        Assert.Equal(10, _repository.Rows.Count); // one Faces + one SceneEmbedding row per photo
        Assert.All(photos, p =>
        {
            var a = loaded[p.Id];
            var face = Assert.Single(a.Faces!.Faces);
            Assert.True(face.Sharpness >= 0);
            Assert.Equal(FakeEmbedder.Vector, a.Embedding);
        });
    }

    [Fact]
    public void FaceSharpness_IsMeasuredForUsableFaces_AndNullForTinyOnes()
    {
        var image = AnalysisImage.Create(200, 100, (x, y) => ((byte)((x * 37) ^ (y * 11)), 0, 0));

        Assert.NotNull(AiAnalysisService.FaceSharpness(image, new DetectedFace(0.1f, 0.1f, 0.2f, 0.4f, 0.9f, new float[10]))); // 40×40 px
        Assert.Null(AiAnalysisService.FaceSharpness(image, new DetectedFace(0.1f, 0.1f, 0.05f, 0.1f, 0.9f, new float[10])));    // 10×10 px
    }

    [Fact]
    public async Task SecondRun_IsCached()
    {
        var photos = Photos(4);
        var service = Service();
        await Run(service, photos);
        var calls = _runtime.Detector.Calls;

        var second = await Run(service, photos);

        Assert.Equal(0, second.Analysed);
        Assert.Equal(4, second.Cached);
        Assert.Equal(calls, _runtime.Detector.Calls);
    }

    [Fact]
    public async Task DifferentModelVersion_InvalidatesCachedResults()
    {
        var photos = Photos(3);
        await Run(Service(), photos);

        _runtime.Detector.VersionTag = "face@v2";
        var summary = await Run(Service(), photos, new AiCapabilities(true, false));

        Assert.Equal(3, summary.Analysed);
        Assert.All(_repository.Rows.Where(r => r.Kind == AnalysisKind.Faces), r => Assert.Equal("face@v2", r.AlgorithmVersion));
    }

    [Fact]
    public async Task ChangedSourceFile_IsReanalysed()
    {
        var photos = Photos(2);
        await Run(Service(), photos);

        photos[0].FileSizeBytes += 1;
        var summary = await Run(Service(), photos);

        Assert.Equal(1, summary.Analysed);
        Assert.Equal(1, summary.Cached);
    }

    [Fact]
    public async Task InferenceFailure_IsIsolatedToThatPhoto()
    {
        var photos = Photos(4);
        _runtime.Detector.FailOn = 2;

        var summary = await Run(Service(), photos, new AiCapabilities(true, false));

        Assert.Equal(3, summary.Analysed);
        Assert.Equal(1, summary.Failed);
        Assert.False(summary.WasCancelled);
    }

    [Fact]
    public async Task UnreadablePhoto_CountsAsFailed()
    {
        var photos = Photos(3);
        _loader.Corrupt.Add(photos[1].FilePath);

        var summary = await Run(Service(), photos);

        Assert.Equal(2, summary.Analysed);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public async Task NoModelsInstalled_DoesNothing_AndReportsAllNotEligible()
    {
        _runtime.Installed = false;
        var photos = Photos(3);

        var summary = await Run(Service(), photos);

        Assert.Equal(3, summary.NotEligible);
        Assert.Equal(0, summary.Analysed);
        Assert.Empty(_repository.Rows);
    }

    [Fact]
    public async Task CapabilitiesOff_LoadReturnsNothing_EvenWithStoredRows()
    {
        var photos = Photos(2);
        var service = Service();
        await Run(service, photos);

        Assert.Empty(await service.LoadAsync(photos, AiCapabilities.None, AnalysisSettings.Default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingPhotos_AreNotEligible()
    {
        var photos = Photos(2);
        photos.Add(AnalysisTestPhotos.Create("gone.jpg", ImportStatus.Missing));

        var summary = await Run(Service(), photos);

        Assert.Equal(2, summary.Analysed);
        Assert.Equal(1, summary.NotEligible);
    }

    [Fact]
    public async Task Cancellation_KeepsCompletedBatches()
    {
        var photos = Photos(9);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _loader.AfterLoad = n =>
        {
            if (n == 3)
            {
                cts.Cancel();
            }
        };

        var summary = await Service().AnalyzeAsync(photos, Both, AnalysisSettings.Default, null, cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.InRange(_repository.Rows.Count, 1, 17);
    }

    private sealed class FakeRuntime : IAiRuntime
    {
        public bool Installed { get; set; } = true;

        public FakeDetector Detector { get; } = new();

        public FakeEmbedder Embedder { get; } = new();

        public string? LastError => Installed ? null : "not installed (test)";

        public IFaceDetector? GetFaceDetector() => Installed ? Detector : null;

        public IImageEmbedder? GetImageEmbedder() => Installed ? Embedder : null;

        public void Reset()
        {
        }
    }

    private sealed class FakeDetector : IFaceDetector
    {
        private int _calls;

        public string VersionTag { get; set; } = "face@v1";

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>1-based call number that throws (simulates an inference error).</summary>
        public int FailOn { get; set; }

        public IReadOnlyList<DetectedFace> Detect(AnalysisImage image, float minScore)
        {
            if (Interlocked.Increment(ref _calls) == FailOn)
            {
                throw new InvalidOperationException("inference failed (test)");
            }

            return [new DetectedFace(0.25f, 0.25f, 0.5f, 0.5f, 0.9f, new float[10])];
        }
    }

    private sealed class FakeEmbedder : IImageEmbedder
    {
        public static readonly float[] Vector = [0.6f, 0.8f, 0f];

        public string VersionTag => "embed@v1";

        public float[] Embed(AnalysisImage image) => (float[])Vector.Clone();
    }
}
