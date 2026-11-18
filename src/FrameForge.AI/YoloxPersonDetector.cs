using FrameForge.Analysis;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameForge.AI;

/// <summary>
/// YOLOX-s person detection (Apache-2.0, OpenCV Zoo 2022nov export), used as in OpenCV Zoo's demo: RGB 0–255, no
/// normalisation, scaled into a 640 × 640 square at the top-left and padded with grey 114. Only COCO class 0 (person)
/// is used. Same preprocessing and decoding as the B1 spike that was graded (ADR 0008).
/// </summary>
internal sealed class YoloxPersonDetector(InferenceSession session, ModelDescriptor model) : IPersonDetector, IDisposable
{
    public const int InputSize = 640;

    private const int PersonClass = 0;
    private const int RowLength = 85;
    private static readonly int[] Strides = [8, 16, 32];

    public string VersionTag { get; } = model.VersionTag;

    public IReadOnlyList<DetectedPerson> Detect(AnalysisImage image, float minScore, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var scale = Math.Min(InputSize / (double)image.Width, InputSize / (double)image.Height);
        var resized = ImageOps.Resize(image, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var span = input.Buffer.Span;
        span.Fill(114f);
        var px = resized.Bgra;
        const int plane = InputSize * InputSize;
        for (var y = 0; y < resized.Height; y++)
        {
            for (var x = 0; x < resized.Width; x++)
            {
                var p = ((y * resized.Width) + x) * 4;
                var i = (y * InputSize) + x;
                span[i] = px[p + 2];             // R
                span[plane + i] = px[p + 1];     // G
                span[(2 * plane) + i] = px[p];   // B
            }
        }

        using var results = OnnxRun.Run(session, [NamedOnnxValue.CreateFromTensor("images", input)], cancellationToken);
        var output = results[0].AsTensor<float>().ToArray();
        return Decode(output, scale, image.Width, image.Height, minScore);
    }

    /// <summary>
    /// People from YOLOX-s output ([8400, 85]: centre x, centre y, log width, log height relative to a grid cell, then
    /// objectness and 80 class scores), normalised to the original image. Score = objectness × person score, as in
    /// OpenCV Zoo's yolox.py; overlapping boxes are merged by non-maximum suppression.
    /// </summary>
    internal static List<DetectedPerson> Decode(ReadOnlySpan<float> output, double scale, int width, int height, float minScore, double nmsIou = 0.5)
    {
        var rows = Strides.Sum(s => (InputSize / s) * (InputSize / s));
        if (output.Length != rows * RowLength)
        {
            throw new ArgumentException($"Expected {rows} × {RowLength} values, got {output.Length}.", nameof(output));
        }

        var candidates = new List<DetectedPerson>();
        var row = 0;
        foreach (var step in Strides)
        {
            var cells = InputSize / step;
            for (var gy = 0; gy < cells; gy++)
            {
                for (var gx = 0; gx < cells; gx++, row++)
                {
                    var d = output.Slice(row * RowLength, RowLength);
                    var score = d[4] * d[5 + PersonClass];
                    if (score < minScore)
                    {
                        continue;
                    }

                    var cx = (d[0] + gx) * step;
                    var cy = (d[1] + gy) * step;
                    var w = MathF.Exp(d[2]) * step;
                    var h = MathF.Exp(d[3]) * step;
                    candidates.Add(new DetectedPerson(
                        new NormalisedBox(
                            Math.Clamp((cx - (w / 2)) / scale / width, 0, 1),
                            Math.Clamp((cy - (h / 2)) / scale / height, 0, 1),
                            Math.Clamp((cx + (w / 2)) / scale / width, 0, 1),
                            Math.Clamp((cy + (h / 2)) / scale / height, 0, 1)),
                        score));
                }
            }
        }

        var kept = new List<DetectedPerson>();
        foreach (var c in candidates.OrderByDescending(c => c.Score))
        {
            if (kept.All(k => Iou(k.Box, c.Box, width, height) <= nmsIou))
            {
                kept.Add(c);
            }
        }

        return kept;
    }

    /// <summary>Intersection over union, measured in pixels so the photo's aspect ratio does not skew it.</summary>
    internal static double Iou(NormalisedBox a, NormalisedBox b, int width, int height)
    {
        var w = (Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * width;
        var h = (Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top)) * height;
        if (w <= 0 || h <= 0)
        {
            return 0;
        }

        var inter = w * h;
        var union = (a.Width * a.Height * width * height) + (b.Width * b.Height * width * height) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    public void Dispose() => session.Dispose();
}
