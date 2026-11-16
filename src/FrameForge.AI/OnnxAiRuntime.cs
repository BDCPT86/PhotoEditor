using FrameForge.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace FrameForge.AI;

/// <summary>
/// Loads installed models on first use (CPU execution provider, limited threads). A missing model returns null;
/// a model that fails to load is logged, remembered in <see cref="LastError"/>, and also returns null — so AI
/// problems never break browsing or culling. Inference sessions are thread-safe for concurrent Run calls.
/// </summary>
public sealed partial class OnnxAiRuntime(IModelStore store, ILogger<OnnxAiRuntime> logger) : IAiRuntime, IDisposable
{
    /// <summary>Threads per inference call; with 2 concurrent photos this caps AI at about 4 cores.</summary>
    public const int IntraOpThreads = 2;

    private readonly Lock _gate = new();
    private YuNetFaceDetector? _faces;
    private DinoV2Embedder? _embedder;
    private bool _facesFailed;
    private bool _embedderFailed;

    public string? LastError { get; private set; }

    public IFaceDetector? GetFaceDetector()
    {
        lock (_gate)
        {
            if (_faces is null && !_facesFailed)
            {
                _faces = Load(AiModelCatalog.FaceDetector, s => new YuNetFaceDetector(s, AiModelCatalog.FaceDetector), ref _facesFailed);
            }

            return _faces;
        }
    }

    public IImageEmbedder? GetImageEmbedder()
    {
        lock (_gate)
        {
            if (_embedder is null && !_embedderFailed)
            {
                _embedder = Load(AiModelCatalog.SceneEmbedder, s => new DinoV2Embedder(s, AiModelCatalog.SceneEmbedder), ref _embedderFailed);
            }

            return _embedder;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _faces?.Dispose();
            _embedder?.Dispose();
            _faces = null;
            _embedder = null;
            _facesFailed = false;
            _embedderFailed = false;
            LastError = null;
        }
    }

    public void Dispose() => Reset();

    private T? Load<T>(ModelDescriptor model, Func<InferenceSession, T> create, ref bool failed)
        where T : class
    {
        var status = store.GetStatus(model);
        if (status.State != ModelState.Installed || status.FilePath is null)
        {
            return null;
        }

        try
        {
            var options = new SessionOptions
            {
                IntraOpNumThreads = IntraOpThreads,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            var session = new InferenceSession(status.FilePath, options);
            LogLoaded(model.VersionTag);
            return create(session);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or InvalidOperationException or ArgumentException)
        {
            failed = true;
            LastError = $"{model.Name} could not be loaded: {ex.Message}";
            LogLoadFailed(ex, model.VersionTag);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Model loaded: {Model} (CPU, {Threads} threads)")]
    private partial void LogLoaded(string model, int threads = IntraOpThreads);

    [LoggerMessage(Level = LogLevel.Error, Message = "Model failed to load: {Model}; the capability is disabled for this session")]
    private partial void LogLoadFailed(Exception exception, string model);
}
