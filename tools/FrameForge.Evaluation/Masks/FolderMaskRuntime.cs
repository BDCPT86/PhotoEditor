using FrameForge.AI;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;

namespace FrameForge.Evaluation.Masks;

/// <summary>
/// The app's subject-mask model classes (YOLOX-s, MobileSAM), loaded from verified files in a folder instead of the
/// model store, with a chosen number of CPU threads per session.
/// </summary>
internal sealed class FolderMaskRuntime : IAiRuntime, IDisposable
{
    private readonly AI.YoloxPersonDetector _detector;
    private readonly MobileSamSegmenter _segmenter;

    public FolderMaskRuntime(string detectorPath, string encoderPath, string decoderPath, int threads = OnnxAiRuntime.MaskIntraOpThreads, bool spin = true)
    {
        _detector = new AI.YoloxPersonDetector(Open(detectorPath, threads, spin), AiModelCatalog.PersonDetector);
        _segmenter = new MobileSamSegmenter(Open(encoderPath, threads, spin), Open(decoderPath, threads, spin), AiModelCatalog.SubjectMaskEncoder, AiModelCatalog.SubjectMaskDecoder);
    }

    public string? LastError => null;

    public IFaceDetector? GetFaceDetector() => null;

    public IImageEmbedder? GetImageEmbedder() => null;

    public IPersonDetector? GetPersonDetector() => _detector;

    public IPromptSegmenter? GetPromptSegmenter() => _segmenter;

    public void Reset()
    {
    }

    public void Dispose()
    {
        _detector.Dispose();
        _segmenter.Dispose();
    }

    private static InferenceSession Open(string path, int threads, bool spin) => new(path, MaskCandidates.Options(threads, spin));
}
