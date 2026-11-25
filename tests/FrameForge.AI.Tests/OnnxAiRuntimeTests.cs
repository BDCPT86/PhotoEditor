using FrameForge.Application.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.AI.Tests;

public sealed class OnnxAiRuntimeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("frameforge-ai-tests-").FullName;

    [Fact]
    public void UsingTheRuntime_TurnsOffOnnxRuntimeTelemetry_ForThisProcess()
    {
        using var runtime = new OnnxAiRuntime(new Store(_ => new ModelStatus(AiModelCatalog.FaceDetector, ModelState.NotInstalled, null)), NullLogger<OnnxAiRuntime>.Instance);

        Assert.Equal("1", Environment.GetEnvironmentVariable(OnnxPrivacy.DisableTelemetryVariable));
    }

    [Fact]
    public void NoModelsInstalled_ReturnsNull_WithoutError()
    {
        using var runtime = new OnnxAiRuntime(new Store(_ => new ModelStatus(AiModelCatalog.FaceDetector, ModelState.NotInstalled, null)), NullLogger<OnnxAiRuntime>.Instance);

        Assert.Null(runtime.GetFaceDetector());
        Assert.Null(runtime.GetImageEmbedder());
        Assert.Null(runtime.LastError);
    }

    [Fact]
    public void CorruptModelFile_ReturnsNull_RecordsError_AndResetAllowsRetry()
    {
        var path = Path.Combine(_dir, "broken.onnx");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var store = new Store(m => new ModelStatus(m, ModelState.Installed, path));
        using var runtime = new OnnxAiRuntime(store, NullLogger<OnnxAiRuntime>.Instance);

        Assert.Null(runtime.GetFaceDetector());
        Assert.Contains("could not be loaded", runtime.LastError, StringComparison.Ordinal);
        Assert.Null(runtime.GetFaceDetector()); // remembered: no second load attempt
        Assert.Equal(1, store.Calls);

        runtime.Reset();
        Assert.Null(runtime.LastError);
        Assert.Null(runtime.GetFaceDetector());
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public void InvalidModelState_IsNotLoaded()
    {
        var store = new Store(m => new ModelStatus(m, ModelState.Invalid, Path.Combine(_dir, "x.onnx")));
        using var runtime = new OnnxAiRuntime(store, NullLogger<OnnxAiRuntime>.Instance);
        Assert.Null(runtime.GetImageEmbedder());
        Assert.Null(runtime.LastError);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Store(Func<ModelDescriptor, ModelStatus> status) : IModelStore
    {
        public int Calls { get; private set; }

        public ModelStatus GetStatus(ModelDescriptor model)
        {
            Calls++;
            return status(model);
        }

        public Task<string> DownloadAsync(ModelDescriptor model, ModelDownloadConsent consent, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Tests never download.");

        public Task<string> ImportAsync(ModelDescriptor model, string sourceFile, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void Remove(ModelDescriptor model) => throw new NotSupportedException();
    }
}
