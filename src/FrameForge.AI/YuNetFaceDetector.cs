using FrameForge.Analysis;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameForge.AI;

/// <summary>
/// YuNet face detector (MIT, OpenCV Zoo, 2026may dynamic-input export). Input: BGR, 0–255 float, no normalisation,
/// zero-padded at the right/bottom to a multiple of 32 (as OpenCV's FaceDetectorYN does). The image is scaled so its
/// longest side is <see cref="InputLongestSide"/>. 1024 rather than 640: on a real 1,010-photo sports set it found 56 %
/// more faces (distant players and spectators) for about 37 ms instead of 15 ms per photo on CPU.
/// </summary>
internal sealed class YuNetFaceDetector(InferenceSession session, ModelDescriptor model) : IFaceDetector, IDisposable
{
    public const int InputLongestSide = 1024;

    /// <summary>Includes the input size, because it changes what is detected (so cached results are redone).</summary>
    public string VersionTag { get; } = $"{model.VersionTag}@in{InputLongestSide}";

    public IReadOnlyList<DetectedFace> Detect(AnalysisImage image, float minScore)
    {
        ArgumentNullException.ThrowIfNull(image);

        var scaled = ImageOps.FitLongest(image, InputLongestSide);
        var paddedW = ((scaled.Width + 31) / 32) * 32;
        var paddedH = ((scaled.Height + 31) / 32) * 32;
        var tensor = new DenseTensor<float>([1, 3, paddedH, paddedW]);
        var px = scaled.Bgra;
        for (var y = 0; y < scaled.Height; y++)
        {
            for (var x = 0; x < scaled.Width; x++)
            {
                var i = ((y * scaled.Width) + x) * 4;
                tensor[0, 0, y, x] = px[i];      // B
                tensor[0, 1, y, x] = px[i + 1];  // G
                tensor[0, 2, y, x] = px[i + 2];  // R
            }
        }

        using var results = session.Run([NamedOnnxValue.CreateFromTensor("input", tensor)]);
        var byName = results.ToDictionary(r => r.Name, r => r.AsTensor<float>().ToArray());
        var outputs = YuNetDecoder.Strides
            .Select(s => new YuNetDecoder.StrideOutput(s, byName[$"cls_{s}"], byName[$"obj_{s}"], byName[$"bbox_{s}"], byName[$"kps_{s}"]))
            .ToList();

        return YuNetDecoder.Decode(outputs, paddedW, scaled.Width, scaled.Height, minScore);
    }

    public void Dispose() => session.Dispose();
}
