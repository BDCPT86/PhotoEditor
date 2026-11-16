using FrameForge.Application.Ai;

namespace FrameForge.AI;

/// <summary>
/// Decodes YuNet's raw per-stride outputs into face boxes, following OpenCV's FaceDetectorYN: for anchor (row r,
/// column c) at stride s, score = sqrt(clamp(cls)·clamp(obj)); centre = ((c, r) + bbox[0..1])·s;
/// size = exp(bbox[2..3])·s; landmark k = ((c, r) + kps[2k..2k+1])·s. Then greedy non-maximum suppression.
/// Pure and deterministic, so it is unit-tested without the model.
/// </summary>
internal static class YuNetDecoder
{
    public static readonly int[] Strides = [8, 16, 32];

    /// <summary>Raw tensors for one stride, flattened (anchors × channels).</summary>
    public sealed record StrideOutput(int Stride, float[] Cls, float[] Obj, float[] Bbox, float[] Kps);

    /// <param name="inputWidth">Padded network input width (multiple of 32).</param>
    /// <param name="imageWidth">Width of the (unpadded) image content the coordinates are normalised against.</param>
    public static List<DetectedFace> Decode(
        IReadOnlyList<StrideOutput> outputs,
        int inputWidth,
        int imageWidth,
        int imageHeight,
        float minScore,
        float nmsIou = 0.3f,
        int topK = 50)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var candidates = new List<(float X1, float Y1, float X2, float Y2, float Score, float[] Kps)>();

        foreach (var o in outputs)
        {
            var cols = inputWidth / o.Stride;
            for (var idx = 0; idx < o.Cls.Length; idx++)
            {
                var score = MathF.Sqrt(Math.Clamp(o.Cls[idx], 0, 1) * Math.Clamp(o.Obj[idx], 0, 1));
                if (score < minScore)
                {
                    continue;
                }

                int r = idx / cols, c = idx % cols;
                var cx = (c + o.Bbox[(idx * 4) + 0]) * o.Stride;
                var cy = (r + o.Bbox[(idx * 4) + 1]) * o.Stride;
                var w = MathF.Exp(o.Bbox[(idx * 4) + 2]) * o.Stride;
                var h = MathF.Exp(o.Bbox[(idx * 4) + 3]) * o.Stride;
                var kps = new float[10];
                for (var k = 0; k < 5; k++)
                {
                    kps[2 * k] = (o.Kps[(idx * 10) + (2 * k)] + c) * o.Stride;
                    kps[(2 * k) + 1] = (o.Kps[(idx * 10) + (2 * k) + 1] + r) * o.Stride;
                }

                candidates.Add((cx - (w / 2), cy - (h / 2), cx + (w / 2), cy + (h / 2), score, kps));
            }
        }

        var kept = new List<(float X1, float Y1, float X2, float Y2, float Score, float[] Kps)>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Y1).ThenBy(c => c.X1))
        {
            if (kept.Count >= topK)
            {
                break;
            }

            if (kept.All(k => Iou(k.X1, k.Y1, k.X2, k.Y2, candidate.X1, candidate.Y1, candidate.X2, candidate.Y2) <= nmsIou))
            {
                kept.Add(candidate);
            }
        }

        return kept.Select(k =>
        {
            float x1 = Math.Clamp(k.X1, 0, imageWidth), y1 = Math.Clamp(k.Y1, 0, imageHeight);
            float x2 = Math.Clamp(k.X2, 0, imageWidth), y2 = Math.Clamp(k.Y2, 0, imageHeight);
            var landmarks = new float[10];
            for (var i = 0; i < 10; i += 2)
            {
                landmarks[i] = Math.Clamp(k.Kps[i] / imageWidth, 0, 1);
                landmarks[i + 1] = Math.Clamp(k.Kps[i + 1] / imageHeight, 0, 1);
            }

            return new DetectedFace(x1 / imageWidth, y1 / imageHeight, (x2 - x1) / imageWidth, (y2 - y1) / imageHeight, k.Score, landmarks);
        })
        .Where(f => f.Width > 0 && f.Height > 0)
        .ToList();
    }

    internal static float Iou(float ax1, float ay1, float ax2, float ay2, float bx1, float by1, float bx2, float by2)
    {
        var iw = Math.Max(0, Math.Min(ax2, bx2) - Math.Max(ax1, bx1));
        var ih = Math.Max(0, Math.Min(ay2, by2) - Math.Max(ay1, by1));
        var inter = iw * ih;
        var union = ((ax2 - ax1) * (ay2 - ay1)) + ((bx2 - bx1) * (by2 - by1)) - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
