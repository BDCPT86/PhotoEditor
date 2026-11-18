using FrameForge.Analysis;
using FrameForge.Application.Ai;

namespace FrameForge.Evaluation.Masks;

/// <summary>A box in pixels of the image the mask is computed on (left, top, right, bottom).</summary>
internal readonly record struct PixelBox(float Left, float Top, float Right, float Bottom);

/// <summary>
/// Pure helpers for the Milestone 08 phase B mask spike: prompts from face boxes, model input sizes, and turning model
/// output into a mask at the image's size. Written to move into FrameForge.AI unchanged once a model is chosen.
/// </summary>
internal static class MaskMath
{
    /// <summary>Long side of the image MobileSAM's encoder sees (the SAM preprocessing size).</summary>
    public const int SamLongSide = 1024;

    /// <summary>Side of SAM's low-resolution mask logits, which cover the padded 1024 × 1024 input.</summary>
    public const int SamLowRes = 256;

    /// <summary>MODNet's reference size: the short side is scaled to this, then both sides round down to a multiple of 32.</summary>
    public const int ModnetRefSize = 512;

    /// <summary>
    /// A loose box around the body that a face belongs to, for prompting SAM. A standing adult is about 7–8 face heights
    /// tall; the box reaches 8.5 face heights below the top of the face and 2.5 face heights to each side (arms out,
    /// a bat or racket), and a little above the head for hair or a cap. Clamped to the image.
    /// </summary>
    public static PixelBox BodyBox(FaceFinding face, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(face);
        var faceHeight = face.Height * height;
        var centreX = (face.X + (face.Width / 2)) * width;
        var top = face.Y * height;
        return new PixelBox(
            (float)Math.Clamp(centreX - (2.5 * faceHeight), 0, width),
            (float)Math.Clamp(top - (0.6 * faceHeight), 0, height),
            (float)Math.Clamp(centreX + (2.5 * faceHeight), 0, width),
            (float)Math.Clamp(top + (8.5 * faceHeight), 0, height));
    }

    /// <summary>The centre of a face in pixels, used as a positive point prompt.</summary>
    public static (float X, float Y) FaceCentre(FaceFinding face, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(face);
        return ((float)((face.X + (face.Width / 2)) * width), (float)((face.Y + (face.Height / 2)) * height));
    }

    // ------------------------------------------------------------------ people (YOLOX-s, round 2)

    /// <summary>YOLOX-s input side: the photo is scaled into this square at the top-left, padded with grey 114.</summary>
    public const int YoloxSize = 640;

    /// <summary>COCO class 0.</summary>
    public const int PersonClass = 0;

    /// <summary>A person counts as a main subject when at least this share of the tallest person's height.</summary>
    public const double MainPersonRelativeHeight = 0.5;

    private static readonly int[] YoloxStrides = [8, 16, 32];

    /// <summary>Scale that fits an image into the YOLOX square (the demo's letterbox ratio).</summary>
    public static double YoloxScale(int width, int height) => Math.Min(YoloxSize / (double)width, YoloxSize / (double)height);

    /// <summary>
    /// People from YOLOX-s output ([8400, 85]: centre x, centre y, log width, log height relative to a grid cell, then
    /// objectness and 80 class scores), as boxes in pixels of the original image. Score = objectness × person score,
    /// as in OpenCV Zoo's yolox.py; overlapping boxes are merged by non-maximum suppression.
    /// </summary>
    public static List<(PixelBox Box, float Score)> DecodePeople(ReadOnlySpan<float> output, double scale, int width, int height, float minScore, float nmsIou = 0.5f)
    {
        const int stride = 85;
        var rows = YoloxStrides.Sum(s => (YoloxSize / s) * (YoloxSize / s));
        if (output.Length != rows * stride)
        {
            throw new ArgumentException($"Expected {rows} × {stride} values, got {output.Length}.", nameof(output));
        }

        var candidates = new List<(PixelBox Box, float Score)>();
        var row = 0;
        foreach (var step in YoloxStrides)
        {
            var cells = YoloxSize / step;
            for (var gy = 0; gy < cells; gy++)
            {
                for (var gx = 0; gx < cells; gx++, row++)
                {
                    var d = output.Slice(row * stride, stride);
                    var score = d[4] * d[5 + PersonClass];
                    if (score < minScore)
                    {
                        continue;
                    }

                    var cx = (d[0] + gx) * step;
                    var cy = (d[1] + gy) * step;
                    var w = MathF.Exp(d[2]) * step;
                    var h = MathF.Exp(d[3]) * step;
                    candidates.Add((new PixelBox(
                        (float)Math.Clamp((cx - (w / 2)) / scale, 0, width),
                        (float)Math.Clamp((cy - (h / 2)) / scale, 0, height),
                        (float)Math.Clamp((cx + (w / 2)) / scale, 0, width),
                        (float)Math.Clamp((cy + (h / 2)) / scale, 0, height)), score));
                }
            }
        }

        var kept = new List<(PixelBox Box, float Score)>();
        foreach (var c in candidates.OrderByDescending(c => c.Score))
        {
            if (kept.All(k => Iou(k.Box, c.Box) <= nmsIou))
            {
                kept.Add(c);
            }
        }

        return kept;
    }

    /// <summary>People large enough to be the subject: at least half the height of the tallest (so distant fielders and
    /// spectators drop out), in left-to-right order.</summary>
    public static List<PixelBox> MainPeople(IReadOnlyList<PixelBox> people)
    {
        ArgumentNullException.ThrowIfNull(people);
        if (people.Count == 0)
        {
            return [];
        }

        var tallest = people.Max(p => p.Bottom - p.Top);
        return people.Where(p => p.Bottom - p.Top >= tallest * MainPersonRelativeHeight).OrderBy(p => p.Left).ToList();
    }

    public static double Iou(PixelBox a, PixelBox b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (w <= 0 || h <= 0)
        {
            return 0;
        }

        var inter = (double)w * h;
        var union = ((a.Right - a.Left) * (a.Bottom - a.Top)) + ((b.Right - b.Left) * (b.Bottom - b.Top)) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    // ------------------------------------------------------------------ input sizes

    /// <summary>Size after scaling the long side to <paramref name="longSide"/>, rounded as SAM's ResizeLongestSide does.</summary>
    public static (int Width, int Height) LongSideSize(int width, int height, int longSide)
    {
        var scale = longSide / (double)Math.Max(width, height);
        return ((int)((width * scale) + 0.5), (int)((height * scale) + 0.5));
    }

    /// <summary>MODNet's input size for an image (from the MODNet demo: short side to 512, both sides floored to ×32).</summary>
    public static (int Width, int Height) ModnetSize(int width, int height)
    {
        int w = width, h = height;
        if (Math.Max(width, height) < ModnetRefSize || Math.Min(width, height) > ModnetRefSize)
        {
            if (width >= height)
            {
                h = ModnetRefSize;
                w = (int)(width / (double)height * ModnetRefSize);
            }
            else
            {
                w = ModnetRefSize;
                h = (int)(height / (double)width * ModnetRefSize);
            }
        }

        return (Math.Max(32, w - (w % 32)), Math.Max(32, h - (h % 32)));
    }

    /// <summary>
    /// SAM's low-resolution logits (256 × 256 over the padded 1024 square) as a mask of 0 or 1 at <paramref name="outWidth"/>
    /// × <paramref name="outHeight"/>. Equivalent to SAM's postprocessing (upscale to 1024, crop the unpadded area, resize)
    /// done as one bilinear sample per output pixel; inside where the logit is above 0.
    /// </summary>
    public static float[] SamMask(ReadOnlySpan<float> lowRes, int resizedWidth, int resizedHeight, int outWidth, int outHeight)
    {
        if (lowRes.Length != SamLowRes * SamLowRes)
        {
            throw new ArgumentException($"Expected {SamLowRes}×{SamLowRes} logits, got {lowRes.Length}.", nameof(lowRes));
        }

        var mask = new float[outWidth * outHeight];
        const double toLow = SamLowRes / (double)SamLongSide;
        for (var y = 0; y < outHeight; y++)
        {
            // Output pixel centre → position in the resized (1024-space) image → low-res pixel index.
            var v = ((y + 0.5) * resizedHeight / outHeight * toLow) - 0.5;
            for (var x = 0; x < outWidth; x++)
            {
                var u = ((x + 0.5) * resizedWidth / outWidth * toLow) - 0.5;
                mask[(y * outWidth) + x] = Bilinear(lowRes, SamLowRes, SamLowRes, u, v) > 0 ? 1f : 0f;
            }
        }

        return mask;
    }

    /// <summary>Bilinear resize of a single-channel map (values kept as they are, edges clamped).</summary>
    public static float[] Resize(ReadOnlySpan<float> source, int width, int height, int outWidth, int outHeight)
    {
        var result = new float[outWidth * outHeight];
        for (var y = 0; y < outHeight; y++)
        {
            var v = ((y + 0.5) * height / outHeight) - 0.5;
            for (var x = 0; x < outWidth; x++)
            {
                result[(y * outWidth) + x] = Bilinear(source, width, height, ((x + 0.5) * width / outWidth) - 0.5, v);
            }
        }

        return result;
    }

    /// <summary>Pixel-wise maximum: the union of several subjects' masks.</summary>
    public static float[] Union(IReadOnlyList<float[]> masks)
    {
        ArgumentNullException.ThrowIfNull(masks);
        if (masks.Count == 0)
        {
            throw new ArgumentException("No masks to combine.", nameof(masks));
        }

        var result = (float[])masks[0].Clone();
        foreach (var mask in masks.Skip(1))
        {
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = Math.Max(result[i], mask[i]);
            }
        }

        return result;
    }

    /// <summary>Share of the image covered by the mask (alpha summed).</summary>
    public static double Coverage(ReadOnlySpan<float> mask)
    {
        double sum = 0;
        foreach (var a in mask)
        {
            sum += a;
        }

        return mask.Length == 0 ? 0 : sum / mask.Length;
    }

    /// <summary>
    /// A picture for judging a mask: inside the mask the photo is unchanged, outside it is darkened and greyed in
    /// proportion to (1 − alpha), and the 50 % edge is outlined in amber so halos and missed limbs are easy to see.
    /// </summary>
    public static AnalysisImage Overlay(AnalysisImage photo, ReadOnlySpan<float> alpha)
    {
        ArgumentNullException.ThrowIfNull(photo);
        int w = photo.Width, h = photo.Height;
        if (alpha.Length != w * h)
        {
            throw new ArgumentException("The mask must match the photo's size.", nameof(alpha));
        }

        var src = photo.Bgra;
        var dst = new byte[src.Length];
        for (var i = 0; i < w * h; i++)
        {
            var p = i * 4;
            var a = Math.Clamp(alpha[i], 0f, 1f);
            var grey = (0.0722f * src[p]) + (0.7152f * src[p + 1]) + (0.2126f * src[p + 2]);
            for (var c = 0; c < 3; c++)
            {
                var outside = grey * 0.3f;
                dst[p + c] = (byte)Math.Clamp((int)MathF.Round((src[p + c] * a) + (outside * (1 - a))), 0, 255);
            }

            dst[p + 3] = 255;
        }

        // Outline where the mask crosses 0.5 (compared with the right and lower neighbours).
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var inside = alpha[(y * w) + x] >= 0.5f;
                var edge = (x + 1 < w && (alpha[(y * w) + x + 1] >= 0.5f) != inside) || (y + 1 < h && (alpha[((y + 1) * w) + x] >= 0.5f) != inside);
                if (edge)
                {
                    var p = ((y * w) + x) * 4;
                    dst[p] = 40;      // B
                    dst[p + 1] = 190; // G
                    dst[p + 2] = 255; // R: amber
                }
            }
        }

        return new AnalysisImage(w, h, dst);
    }

    private static float Bilinear(ReadOnlySpan<float> map, int width, int height, double u, double v)
    {
        var fx = Math.Clamp(u, 0, width - 1);
        var fy = Math.Clamp(v, 0, height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        var wx = (float)(fx - x0);
        var wy = (float)(fy - y0);
        var top = map[(y0 * width) + x0] + ((map[(y0 * width) + x1] - map[(y0 * width) + x0]) * wx);
        var bottom = map[(y1 * width) + x0] + ((map[(y1 * width) + x1] - map[(y1 * width) + x0]) * wx);
        return top + ((bottom - top) * wy);
    }
}
