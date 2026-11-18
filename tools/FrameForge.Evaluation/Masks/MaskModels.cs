using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using FrameForge.AI;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameForge.Evaluation.Masks;

/// <summary>A candidate model file for the mask spike, pinned by size and SHA-256 (licences in docs/decisions/0008).</summary>
internal sealed record MaskModelFile(string FileName, long SizeBytes, string Sha256, string Source);

/// <summary>The subject-mask candidates compared in Milestone 08 phase B, step B1. Not in the app's catalogue yet.</summary>
internal static class MaskCandidates
{
    public static MaskModelFile SamEncoder { get; } = new(
        "mobile_sam.encoder.onnx", 28_157_853, "8c1494f7dc70b61b15bc7ab8e4291804d47294d881da137b099b04c1776535dd",
        "https://huggingface.co/nrl-ai/samexporter-onnx-models/resolve/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam/mobile_sam.encoder.onnx");

    public static MaskModelFile SamDecoder { get; } = new(
        "mobile_sam.decoder.onnx", 16_502_895, "23c11087a0c1930d863ba37fdf0f2b0080f5a94269191f679ffd16b8415f0a38",
        "https://huggingface.co/nrl-ai/samexporter-onnx-models/resolve/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam/mobile_sam.decoder.onnx");

    public static MaskModelFile Modnet { get; } = new(
        "modnet.onnx", 25_888_640, "07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9",
        "https://huggingface.co/Xenova/modnet/resolve/fa2fa546052fba4c08921230a26cc69a333fca12/onnx/model.onnx");

    public static MaskModelFile PersonDetector { get; } = new(
        "object_detection_yolox_2022nov.onnx", 35_858_002, "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063",
        "https://media.githubusercontent.com/media/opencv/opencv_zoo/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/object_detection_yolox/object_detection_yolox_2022nov.onnx");

    /// <summary>The file in <paramref name="folder"/> whose size and SHA-256 match, or null.</summary>
    public static string? Find(string folder, MaskModelFile model)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.onnx").Where(f => new FileInfo(f).Length == model.SizeBytes))
        {
            using var stream = File.OpenRead(file);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) == model.Sha256)
            {
                return file;
            }
        }

        return null;
    }

    internal static SessionOptions Options() => new() { IntraOpNumThreads = OnnxAiRuntime.IntraOpThreads };
}

/// <summary>A subject mask at the size of the image it was computed on (alpha 0–1), with how long each part took.</summary>
internal sealed record MaskResult(float[] Alpha, int Width, int Height, double EncodeMs, double DecodeMs);

/// <summary>
/// MobileSAM (Apache-2.0), samexporter ONNX export with preprocessing inside the encoder: the encoder takes RGB 0–255
/// as height × width × 3 floats, long side scaled to 1024; the decoder takes points in that scaled space. Each subject
/// prompts one decode, and the masks are combined.
/// <list type="bullet">
/// <item>Round 1 (<see cref="Mask(AnalysisImage, IReadOnlyList{FaceFinding})"/>): a body box guessed from each main face
/// (corners labelled 2 and 3) plus the face centre as a positive point.</item>
/// <item>Round 2 (<see cref="Mask(AnalysisImage, IReadOnlyList{PixelBox})"/>): each main person's box from the person
/// detector, corners only.</item>
/// </list>
/// </summary>
internal sealed class MobileSamMasker(string encoderPath, string decoderPath) : IDisposable
{
    /// <summary>Face centre (positive point), box top-left (2), box bottom-right (3), padding point (−1).</summary>
    private static readonly float[] FaceLabels = [1f, 2f, 3f, -1f];

    /// <summary>Box top-left (2), box bottom-right (3), padding point (−1).</summary>
    private static readonly float[] BoxLabels = [2f, 3f, -1f];

    private readonly InferenceSession _encoder = new(encoderPath, MaskCandidates.Options());
    private readonly InferenceSession _decoder = new(decoderPath, MaskCandidates.Options());

    public MaskResult Mask(AnalysisImage image, IReadOnlyList<FaceFinding> mainFaces)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(mainFaces);
        return Run(image, mainFaces.Select(face =>
        {
            var box = MaskMath.BodyBox(face, image.Width, image.Height);
            var (fx, fy) = MaskMath.FaceCentre(face, image.Width, image.Height);
            return (new[] { fx, fy, box.Left, box.Top, box.Right, box.Bottom }, FaceLabels);
        }).ToList());
    }

    public MaskResult Mask(AnalysisImage image, IReadOnlyList<PixelBox> people)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(people);
        return Run(image, people.Select(b => (new[] { b.Left, b.Top, b.Right, b.Bottom }, BoxLabels)).ToList());
    }

    /// <param name="prompts">Per subject: points as x, y pairs in image pixels (without the padding point), and their labels (with it).</param>
    private MaskResult Run(AnalysisImage image, List<(float[] Points, float[] Labels)> prompts)
    {
        var sw = Stopwatch.StartNew();
        var (rw, rh) = MaskMath.LongSideSize(image.Width, image.Height, MaskMath.SamLongSide);
        var resized = ImageOps.Resize(image, rw, rh);
        var input = new DenseTensor<float>([rh, rw, 3]);
        var span = input.Buffer.Span;
        var px = resized.Bgra;
        for (int i = 0, p = 0; i < rw * rh; i++, p += 4)
        {
            span[(i * 3) + 0] = px[p + 2]; // R
            span[(i * 3) + 1] = px[p + 1]; // G
            span[(i * 3) + 2] = px[p];     // B
        }

        using var encoded = _encoder.Run([NamedOnnxValue.CreateFromTensor("input_image", input)]);
        var embeddings = encoded[0].AsTensor<float>().ToDenseTensor();
        var encodeMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var masks = new List<float[]>();
        var sx = rw / (float)image.Width;
        var sy = rh / (float)image.Height;
        foreach (var (points, labels) in prompts)
        {
            var count = labels.Length;
            var coords = new float[count * 2];
            for (var i = 0; i < points.Length; i += 2)
            {
                coords[i] = points[i] * sx;
                coords[i + 1] = points[i + 1] * sy;
            }

            using var decoded = _decoder.Run(
            [
                NamedOnnxValue.CreateFromTensor("image_embeddings", embeddings),
                NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coords, [1, count, 2])),
                NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>((float[])labels.Clone(), [1, count])),
                NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>([1, 1, MaskMath.SamLowRes, MaskMath.SamLowRes])),
                NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>([1])),
                NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new[] { (float)rh, rw }, [2])),
            ]);
            var low = decoded.First(r => r.Name == "low_res_masks").AsTensor<float>().ToArray();
            masks.Add(MaskMath.SamMask(low, rw, rh, image.Width, image.Height));
        }

        var alpha = masks.Count == 0 ? new float[image.Width * image.Height] : MaskMath.Union(masks);
        return new MaskResult(alpha, image.Width, image.Height, encodeMs, sw.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}

/// <summary>
/// YOLOX-s person detection (Apache-2.0, OpenCV Zoo 2022nov export), following OpenCV Zoo's demo: RGB 0–255, no
/// normalisation, scaled into a 640 × 640 square at the top-left and padded with grey 114. Only COCO class 0 (person)
/// is used.
/// </summary>
internal sealed class YoloxPersonDetector(string modelPath) : IDisposable
{
    /// <summary>OpenCV Zoo's default confidence threshold for YOLOX.</summary>
    public const float MinScore = 0.5f;

    private readonly InferenceSession _session = new(modelPath, MaskCandidates.Options());

    public (List<(PixelBox Box, float Score)> People, double Ms) Detect(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sw = Stopwatch.StartNew();
        var scale = MaskMath.YoloxScale(image.Width, image.Height);
        int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
        var resized = ImageOps.Resize(image, Math.Max(1, w), Math.Max(1, h));
        const int size = MaskMath.YoloxSize;
        var input = new DenseTensor<float>([1, 3, size, size]);
        var span = input.Buffer.Span;
        span.Fill(114f);
        var px = resized.Bgra;
        for (var y = 0; y < resized.Height; y++)
        {
            for (var x = 0; x < resized.Width; x++)
            {
                var p = ((y * resized.Width) + x) * 4;
                var i = (y * size) + x;
                span[i] = px[p + 2];                       // R
                span[(size * size) + i] = px[p + 1];       // G
                span[(2 * size * size) + i] = px[p];       // B
            }
        }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("images", input)]);
        var output = results[0].AsTensor<float>().ToArray();
        var people = MaskMath.DecodePeople(output, scale, image.Width, image.Height, MinScore);
        return (people, sw.Elapsed.TotalMilliseconds);
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// MODNet portrait matting (Apache-2.0), Xenova ONNX export: RGB normalised to −1…1, short side scaled to 512 and both
/// sides floored to a multiple of 32; outputs an alpha matte. It takes no prompt, so it finds whichever people look
/// most like a portrait subject.
/// </summary>
internal sealed class ModnetMasker(string modelPath) : IDisposable
{
    private readonly InferenceSession _session = new(modelPath, MaskCandidates.Options());

    public MaskResult Mask(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sw = Stopwatch.StartNew();
        var (w, h) = MaskMath.ModnetSize(image.Width, image.Height);
        var resized = ImageOps.Resize(image, w, h);
        var input = new DenseTensor<float>([1, 3, h, w]);
        var px = resized.Bgra;
        var span = input.Buffer.Span;
        for (int i = 0, p = 0; i < w * h; i++, p += 4)
        {
            span[i] = (px[p + 2] / 127.5f) - 1f;               // R
            span[(w * h) + i] = (px[p + 1] / 127.5f) - 1f;     // G
            span[(2 * w * h) + i] = (px[p] / 127.5f) - 1f;     // B
        }

        using var results = _session.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
        var matte = results[0].AsTensor<float>().ToArray();
        var alpha = MaskMath.Resize(matte, w, h, image.Width, image.Height);
        return new MaskResult(alpha, image.Width, image.Height, sw.Elapsed.TotalMilliseconds, 0);
    }

    public void Dispose() => _session.Dispose();
}
