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

    /// <summary>
    /// Threads for the mask models (YOLOX-s, MobileSAM). One mask runs at a time and the user is waiting for it, so
    /// they get more: 4 threads made a subject mask about 1.8× faster than 2 on a 14-core laptop, and 6 was slower
    /// than 4 (B7 benchmark, ADR 0014).
    /// </summary>
    public const int MaskIntraOpThreads = 4;

    static OnnxAiRuntime() => OnnxPrivacy.EnsureApplied(); // before any session is created

    private readonly Lock _gate = new();
    private YuNetFaceDetector? _faces;
    private DinoV2Embedder? _embedder;
    private YoloxPersonDetector? _people;
    private MobileSamSegmenter? _segmenter;
    private bool _facesFailed;
    private bool _embedderFailed;
    private bool _peopleFailed;
    private bool _segmenterFailed;

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

    public IPersonDetector? GetPersonDetector()
    {
        lock (_gate)
        {
            if (_people is null && !_peopleFailed)
            {
                _people = Load(AiModelCatalog.PersonDetector, s => new YoloxPersonDetector(s, AiModelCatalog.PersonDetector), ref _peopleFailed, MaskIntraOpThreads);
            }

            return _people;
        }
    }

    public IPromptSegmenter? GetPromptSegmenter()
    {
        lock (_gate)
        {
            // Check both files first, so a missing decoder never loads the encoder on every call.
            if (_segmenter is null && !_segmenterFailed
                && store.GetStatus(AiModelCatalog.SubjectMaskEncoder).State == ModelState.Installed
                && store.GetStatus(AiModelCatalog.SubjectMaskDecoder).State == ModelState.Installed)
            {
                // Both halves are needed; if the decoder is missing or fails, the encoder is released again.
                var encoder = Load(AiModelCatalog.SubjectMaskEncoder, s => s, ref _segmenterFailed, MaskIntraOpThreads);
                var decoder = encoder is null ? null : Load(AiModelCatalog.SubjectMaskDecoder, s => s, ref _segmenterFailed, MaskIntraOpThreads);
                if (encoder is not null && decoder is not null)
                {
                    _segmenter = new MobileSamSegmenter(encoder, decoder, AiModelCatalog.SubjectMaskEncoder, AiModelCatalog.SubjectMaskDecoder);
                }
                else
                {
                    encoder?.Dispose();
                }
            }

            return _segmenter;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _faces?.Dispose();
            _embedder?.Dispose();
            _people?.Dispose();
            _segmenter?.Dispose();
            _faces = null;
            _embedder = null;
            _people = null;
            _segmenter = null;
            _facesFailed = false;
            _embedderFailed = false;
            _peopleFailed = false;
            _segmenterFailed = false;
            LastError = null;
        }
    }

    public void Dispose() => Reset();

    private T? Load<T>(ModelDescriptor model, Func<InferenceSession, T> create, ref bool failed, int threads = IntraOpThreads)
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
                IntraOpNumThreads = threads,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            var session = new InferenceSession(status.FilePath, options);
            LogLoaded(model.VersionTag, threads);
            return create(session);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or InvalidOperationException or ArgumentException)
        {
            failed = true;
            LastError = $"{model.Name} could not be loaded: {ex.Message}";
            LogLoadFailed(ex, model.VersionTag);
            return null;
        }
        catch (Exception ex) when (OnnxNative.IsLoadFailure(ex))
        {
            // The native runtime itself is missing or unusable: AI is off, everything else carries on.
            failed = true;
            LastError = $"{model.Name} could not be loaded: {OnnxNative.Explanation}";
            LogLoadFailed(ex, model.VersionTag);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Model loaded: {Model} (CPU, {Threads} threads)")]
    private partial void LogLoaded(string model, int threads);

    [LoggerMessage(Level = LogLevel.Error, Message = "Model failed to load: {Model}; the capability is disabled for this session")]
    private partial void LogLoadFailed(Exception exception, string model);
}
