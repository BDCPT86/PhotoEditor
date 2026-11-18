using System.Security.Cryptography;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace FrameForge.AI.Tests;

/// <summary>The subject mask models' pre- and postprocessing (Milestone 08 phase B, B3).</summary>
public sealed class MaskModelTests
{
    private const int Rows = 8400;
    private const int RowLength = 85;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A YOLOX output with one confident person in cell (gx, gy) of stride 8.</summary>
    private static float[] YoloxOutput(int gx, int gy, float logW, float logH, float objectness = 0.9f, float person = 0.9f)
    {
        var output = new float[Rows * RowLength];
        var row = (gy * 80) + gx;
        output[(row * RowLength) + 0] = 0.5f;
        output[(row * RowLength) + 1] = 0.5f;
        output[(row * RowLength) + 2] = logW;
        output[(row * RowLength) + 3] = logH;
        output[(row * RowLength) + 4] = objectness;
        output[(row * RowLength) + 5] = person;
        return output;
    }

    [Fact]
    public void Yolox_DecodesAPersonIntoTheOriginalImagesCoordinates()
    {
        // Centre (40 + 0.5) × 8 = 324 px in the 640 square; width e^2 × 8 ≈ 59 px. Photo 1280 × 960 → scale 0.5.
        var people = YoloxPersonDetector.Decode(YoloxOutput(40, 30, 2f, 3f), 0.5, 1280, 960, 0.5f);

        var person = Assert.Single(people);
        Assert.Equal(0.81, person.Score, 3);
        var w = Math.Exp(2) * 8 / 0.5;
        Assert.Equal(((324 / 0.5) - (w / 2)) / 1280, person.Box.Left, 4);
        Assert.Equal(((324 / 0.5) + (w / 2)) / 1280, person.Box.Right, 4);
    }

    [Fact]
    public void Yolox_ScoreIsObjectnessTimesPerson_BelowTheThresholdIsDropped() =>
        Assert.Empty(YoloxPersonDetector.Decode(YoloxOutput(10, 10, 1f, 1f, objectness: 0.6f, person: 0.6f), 1, 640, 640, 0.5f));

    [Fact]
    public void Yolox_RefusesOutputOfTheWrongSize() =>
        Assert.Throws<ArgumentException>(() => YoloxPersonDetector.Decode(new float[10], 1, 640, 640, 0.5f));

    [Fact]
    public void Iou_IsMeasuredInPixels()
    {
        var a = new NormalisedBox(0, 0, 0.5, 1);
        var b = new NormalisedBox(0.25, 0, 0.75, 1);

        Assert.Equal(1 / 3.0, YoloxPersonDetector.Iou(a, b, 2000, 500), 9);
        Assert.Equal(0, YoloxPersonDetector.Iou(a, new NormalisedBox(0.6, 0, 1, 1), 2000, 500));
    }

    [Fact]
    public void Sam_PromptPoints_PutClicksThenBoxCornersThenThePaddingPoint()
    {
        var prompt = new SegmentPrompt(new NormalisedBox(0.1, 0.2, 0.5, 0.9), [new MaskPoint(0.3, 0.4, true), new MaskPoint(0.45, 0.85, false)]);

        var (coords, labels) = MobileSamSegmenter.PromptPoints(prompt, 1000, 500);

        Assert.Equal([1f, 0f, 2f, 3f, -1f], labels);
        Assert.Equal([300f, 200f, 450f, 425f, 100f, 100f, 500f, 450f, 0f, 0f], coords, new ToleranceComparer());
    }

    [Fact]
    public void Sam_LongSideSize_RoundsAsSamDoes() =>
        Assert.Equal((1024, 683), MobileSamSegmenter.LongSideSize(6000, 4000));

    [Fact]
    public void Sam_UpscaledLogits_AreInsideWhereAboveZero_AndIgnoreThePadding()
    {
        // Resized image 1024 × 512 covers the top half of the 1024 square, i.e. rows 0–127 of the logits.
        var logits = new float[256 * 256];
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                logits[(y * 256) + x] = x < 128 ? 5f : -5f;
            }
        }

        var mask = MobileSamSegmenter.UpscaleLogits(logits, 1024, 512, 200, 100);

        Assert.Equal((200, 100), (mask.Width, mask.Height));
        Assert.Equal(255, mask.Alpha[(50 * 200) + 10]);
        Assert.Equal(0, mask.Alpha[(50 * 200) + 190]);
        Assert.Equal(0.5, mask.Coverage(), 2);
    }

    [Fact]
    public void Runtime_WithoutTheSubjectModels_ReturnsNull_AndNeverLoadsHalfOfSam()
    {
        var calls = new List<string>();
        var store = new Store(m =>
        {
            calls.Add(m.Id);
            return new ModelStatus(m, m == AiModelCatalog.SubjectMaskEncoder ? ModelState.Installed : ModelState.NotInstalled, m == AiModelCatalog.SubjectMaskEncoder ? "missing.onnx" : null);
        });
        using var runtime = new OnnxAiRuntime(store, NullLogger<OnnxAiRuntime>.Instance);

        Assert.Null(runtime.GetPersonDetector());
        Assert.Null(runtime.GetPromptSegmenter());
        Assert.Null(runtime.LastError); // the encoder file was never opened
        Assert.Contains("Install them in AI models", new SubjectMaskGenerator(runtime).UnavailableReason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ real models (FRAMEFORGE_TEST_MODELS)

    private static string? Find(ModelDescriptor model)
    {
        var dir = Environment.GetEnvironmentVariable(RealModelTests.ModelsVariable);
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, model.FileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    private static InferenceSession Open(string path, ModelDescriptor model)
    {
        using (var stream = File.OpenRead(path))
        {
            Assert.Equal(model.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        return new InferenceSession(path, new SessionOptions { IntraOpNumThreads = OnnxAiRuntime.IntraOpThreads });
    }

    [Fact]
    public void RealModels_SegmentAClickedShape_AndCancelStopsThem()
    {
        var paths = AiModelCatalog.SubjectMaskModels.Select(Find).ToList();
        Assert.SkipWhen(paths.Any(p => p is null), $"Set {RealModelTests.ModelsVariable} to a folder with the three subject mask model files.");

        using var detector = new YoloxPersonDetector(Open(paths[0]!, AiModelCatalog.PersonDetector), AiModelCatalog.PersonDetector);
        using var segmenter = new MobileSamSegmenter(
            Open(paths[1]!, AiModelCatalog.SubjectMaskEncoder), Open(paths[2]!, AiModelCatalog.SubjectMaskDecoder),
            AiModelCatalog.SubjectMaskEncoder, AiModelCatalog.SubjectMaskDecoder);

        // A dark disc on a light background: no person, but a click on the disc should select it.
        var image = AnalysisImage.Create(640, 480, (x, y) => ((x - 320) * (x - 320)) + ((y - 240) * (y - 240)) < 80 * 80 ? ((byte)30, (byte)40, (byte)160) : ((byte)230, (byte)225, (byte)215));

        Assert.Empty(detector.Detect(image, 0.5f, Ct));
        var mask = Assert.Single(segmenter.Segment(image, [new SegmentPrompt(null, [new MaskPoint(0.5, 0.5, true)])], Ct));
        Assert.Equal(255, mask.Alpha[(240 * 640) + 320]);
        Assert.Equal(0, mask.Alpha[(20 * 640) + 20]);
        var disc = Math.PI * 80 * 80 / (640 * 480);
        Assert.InRange(mask.Coverage(), disc * 0.8, disc * 1.2);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => segmenter.Segment(image, [new SegmentPrompt(null, [new MaskPoint(0.5, 0.5, true)])], cancelled.Token));
    }

    private sealed class ToleranceComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) < 1e-3f;

        public int GetHashCode(float obj) => 0;
    }

    private sealed class Store(Func<ModelDescriptor, ModelStatus> status) : IModelStore
    {
        public ModelStatus GetStatus(ModelDescriptor model) => status(model);

        public Task<string> DownloadAsync(ModelDescriptor model, ModelDownloadConsent consent, IProgress<double>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ImportAsync(ModelDescriptor model, string sourceFile, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Remove(ModelDescriptor model) => throw new NotSupportedException();
    }
}
