using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Rendering;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameForge.AI;

/// <summary>
/// MobileSAM (Apache-2.0), samexporter ONNX export with preprocessing inside the encoder. The encoder takes RGB 0–255
/// as height × width × 3 floats with the long side scaled to 1024, once per photo. The decoder takes, per prompt, points
/// in that scaled space: box corners (labels 2 and 3), Include clicks (1), Exclude clicks (0), and a padding point (−1)
/// as in the graded B1 spike. Its 256 × 256 low-resolution logits are upscaled here, one bilinear sample per pixel,
/// and the mask is where the logit is above 0.
/// </summary>
internal sealed class MobileSamSegmenter(InferenceSession encoder, InferenceSession decoder, ModelDescriptor encoderModel, ModelDescriptor decoderModel)
    : IPromptSegmenter, IDisposable
{
    public const int LongSide = 1024;
    public const int LowRes = 256;

    private readonly Lock _gate = new();

    // The last photo's encoding (seconds of CPU) is kept, so refining it with clicks only runs the decoder.
    private (AnalysisImage Image, DenseTensor<float> Embeddings)? _last;

    public string VersionTag { get; } = $"{encoderModel.VersionTag}+{decoderModel.VersionTag}";

    /// <summary>How many times the encoder ran (for tests of the encoding cache).</summary>
    internal int Encodes { get; private set; }

    public IReadOnlyList<MaskImage> Segment(AnalysisImage image, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(prompts);
        if (prompts.Count == 0)
        {
            return [];
        }

        var (rw, rh) = LongSideSize(image.Width, image.Height);
        var embeddings = Encode(image, rw, rh, cancellationToken);

        var masks = new List<MaskImage>(prompts.Count);
        foreach (var prompt in prompts)
        {
            var (coords, labels) = PromptPoints(prompt, rw, rh);
            using var decoded = OnnxRun.Run(
                decoder,
                [
                    NamedOnnxValue.CreateFromTensor("image_embeddings", embeddings),
                    NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coords, [1, labels.Length, 2])),
                    NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, [1, labels.Length])),
                    NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>([1, 1, LowRes, LowRes])),
                    NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>([1])),
                    NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new[] { (float)rh, rw }, [2])),
                ],
                cancellationToken);
            var low = decoded.First(r => r.Name == "low_res_masks").AsTensor<float>().ToArray();
            masks.Add(UpscaleLogits(low, rw, rh, image.Width, image.Height));
        }

        return masks;
    }

    /// <summary>The image's encoding, reused when the same image (the same object) is segmented again.</summary>
    private DenseTensor<float> Encode(AnalysisImage image, int rw, int rh, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_last is { } last && ReferenceEquals(last.Image, image))
            {
                return last.Embeddings;
            }
        }

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

        using var encoded = OnnxRun.Run(encoder, [NamedOnnxValue.CreateFromTensor("input_image", input)], cancellationToken);
        var embeddings = encoded[0].AsTensor<float>().ToDenseTensor();
        lock (_gate)
        {
            _last = (image, embeddings);
            Encodes++;
        }

        return embeddings;
    }

    /// <summary>Size after scaling the long side to 1024, rounded as SAM's ResizeLongestSide does.</summary>
    internal static (int Width, int Height) LongSideSize(int width, int height)
    {
        var scale = LongSide / (double)Math.Max(width, height);
        return ((int)((width * scale) + 0.5), (int)((height * scale) + 0.5));
    }

    /// <summary>Decoder points (x, y pairs in the resized image) and labels for a prompt, ending with the padding point.</summary>
    internal static (float[] Coords, float[] Labels) PromptPoints(SegmentPrompt prompt, int resizedWidth, int resizedHeight)
    {
        var coords = new List<float>();
        var labels = new List<float>();
        foreach (var p in prompt.Points)
        {
            coords.Add((float)(p.X * resizedWidth));
            coords.Add((float)(p.Y * resizedHeight));
            labels.Add(p.Include ? 1f : 0f);
        }

        if (prompt.Box is { } box)
        {
            coords.AddRange([(float)(box.Left * resizedWidth), (float)(box.Top * resizedHeight), (float)(box.Right * resizedWidth), (float)(box.Bottom * resizedHeight)]);
            labels.AddRange([2f, 3f]);
        }

        coords.AddRange([0f, 0f]);
        labels.Add(-1f);
        return ([.. coords], [.. labels]);
    }

    /// <summary>
    /// SAM's low-resolution logits (256 × 256 over the padded 1024 square) as a 0/255 mask at the output size: the
    /// same as SAM's postprocessing (upscale to 1024, crop the unpadded area, resize), done as one bilinear sample per
    /// output pixel.
    /// </summary>
    internal static MaskImage UpscaleLogits(ReadOnlySpan<float> lowRes, int resizedWidth, int resizedHeight, int outWidth, int outHeight)
    {
        if (lowRes.Length != LowRes * LowRes)
        {
            throw new ArgumentException($"Expected {LowRes}×{LowRes} logits, got {lowRes.Length}.", nameof(lowRes));
        }

        var alpha = new byte[outWidth * outHeight];
        const double toLow = LowRes / (double)LongSide;
        for (var y = 0; y < outHeight; y++)
        {
            var v = Math.Clamp(((y + 0.5) * resizedHeight / outHeight * toLow) - 0.5, 0, LowRes - 1);
            int y0 = (int)v, y1 = Math.Min(y0 + 1, LowRes - 1);
            var wy = (float)(v - y0);
            for (var x = 0; x < outWidth; x++)
            {
                var u = Math.Clamp(((x + 0.5) * resizedWidth / outWidth * toLow) - 0.5, 0, LowRes - 1);
                int x0 = (int)u, x1 = Math.Min(x0 + 1, LowRes - 1);
                var wx = (float)(u - x0);
                var top = lowRes[(y0 * LowRes) + x0] + ((lowRes[(y0 * LowRes) + x1] - lowRes[(y0 * LowRes) + x0]) * wx);
                var bottom = lowRes[(y1 * LowRes) + x0] + ((lowRes[(y1 * LowRes) + x1] - lowRes[(y1 * LowRes) + x0]) * wx);
                alpha[(y * outWidth) + x] = top + ((bottom - top) * wy) > 0 ? (byte)255 : (byte)0;
            }
        }

        return new MaskImage(outWidth, outHeight, alpha);
    }

    public void Dispose()
    {
        encoder.Dispose();
        decoder.Dispose();
    }
}
