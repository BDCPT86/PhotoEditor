using System.Diagnostics;
using System.Security.Cryptography;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;

namespace FrameForge.AI.Tests;

/// <summary>
/// Runs the real model files when the folder named by FRAMEFORGE_TEST_MODELS contains them (they are not in the
/// repository and are never downloaded by tests). Skipped otherwise. These check wiring and determinism on synthetic
/// images; they are not quality evaluations.
/// </summary>
public sealed class RealModelTests
{
    public const string ModelsVariable = "FRAMEFORGE_TEST_MODELS";

    private static string? ModelFile(ModelDescriptor model, string sourceName)
    {
        var dir = Environment.GetEnvironmentVariable(ModelsVariable);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var path = new[] { model.FileName, sourceName }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        return path;
    }

    private static InferenceSession Open(string path, ModelDescriptor model)
    {
        using (var stream = File.OpenRead(path))
        {
            Assert.Equal(model.Sha256, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        return new InferenceSession(path, new SessionOptions { IntraOpNumThreads = OnnxAiRuntime.IntraOpThreads });
    }

    private static AnalysisImage Scene(int seed, int shift = 0) => AnalysisImage.Create(640, 480, (x, y) =>
    {
        var sx = x + shift;
        return ((byte)((sx * (3 + seed)) ^ (y * 2)), (byte)((y * (5 + seed)) + (sx / 4)), (byte)(((sx + y) * (1 + seed)) % 256));
    });

    [Fact]
    public void YuNet_RunsOnSyntheticImage_FindsNoFaceInNoise()
    {
        var path = ModelFile(AiModelCatalog.FaceDetector, "face_detection_yunet_2026may.onnx");
        Assert.SkipWhen(path is null, $"Set {ModelsVariable} to a folder with the YuNet model to run this test.");

        using var detector = new YuNetFaceDetector(Open(path!, AiModelCatalog.FaceDetector), AiModelCatalog.FaceDetector);
        var image = Scene(1);
        var faces = detector.Detect(image, 0.6f);
        var again = detector.Detect(image, 0.6f);

        Assert.Empty(faces);
        Assert.Equal(faces.Count, again.Count);
    }

    [Fact]
    public void Dino_EmbeddingsAreUnitLength_Deterministic_AndSimilarForShiftedScene()
    {
        var path = ModelFile(AiModelCatalog.SceneEmbedder, "dinov2-small-model.onnx");
        Assert.SkipWhen(path is null, $"Set {ModelsVariable} to a folder with the DINOv2-small model to run this test.");

        using var embedder = new DinoV2Embedder(Open(path!, AiModelCatalog.SceneEmbedder), AiModelCatalog.SceneEmbedder);
        var sw = Stopwatch.StartNew();
        var a = embedder.Embed(Scene(1));
        var elapsed = sw.Elapsed;
        var a2 = embedder.Embed(Scene(1));
        var shifted = embedder.Embed(Scene(1, shift: 12));
        var other = embedder.Embed(Scene(7));

        Assert.Equal(384, a.Length);
        Assert.Equal(1, EmbeddingGrouper.Cosine(a, a), 3);
        Assert.Equal(a, a2);
        Assert.True(EmbeddingGrouper.Cosine(a, shifted) > EmbeddingGrouper.Cosine(a, other),
            $"shifted {EmbeddingGrouper.Cosine(a, shifted):0.000} vs other {EmbeddingGrouper.Cosine(a, other):0.000}");
        TestContext.Current.SendDiagnosticMessage($"DINOv2 first embed {elapsed.TotalMilliseconds:0} ms");
    }
}
