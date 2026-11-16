using FrameForge.Analysis;
using FrameForge.Application.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FrameForge.AI;

/// <summary>
/// DINOv2-small image embedding (Apache-2.0). Preprocessing follows the model's processor config: resize shortest
/// edge to 256, centre-crop 224×224, RGB scaled to 0–1, normalised with ImageNet mean/std. Resampling here is
/// bilinear (the reference uses bicubic), a small documented difference. The embedding is the CLS token
/// (index 0 of <c>last_hidden_state</c>, 384 values), L2-normalised.
/// </summary>
internal sealed class DinoV2Embedder(InferenceSession session, ModelDescriptor model) : IImageEmbedder, IDisposable
{
    public const int ResizeShortest = 256;
    public const int Crop = 224;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    public string VersionTag { get; } = model.VersionTag;

    public float[] Embed(AnalysisImage image)
    {
        var tensor = Preprocess(image);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor("pixel_values", tensor)]);
        var hidden = results.First(r => r.Name == "last_hidden_state").AsTensor<float>();
        var dims = hidden.Dimensions[2];
        var cls = new float[dims];
        for (var i = 0; i < dims; i++)
        {
            cls[i] = hidden[0, 0, i];
        }

        return Normalize(cls);
    }

    internal static DenseTensor<float> Preprocess(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var scale = (float)ResizeShortest / Math.Min(image.Width, image.Height);
        var resized = ImageOps.Resize(image, Math.Max(Crop, (int)MathF.Round(image.Width * scale)), Math.Max(Crop, (int)MathF.Round(image.Height * scale)));
        var left = (resized.Width - Crop) / 2;
        var top = (resized.Height - Crop) / 2;

        var tensor = new DenseTensor<float>([1, 3, Crop, Crop]);
        var px = resized.Bgra;
        for (var y = 0; y < Crop; y++)
        {
            for (var x = 0; x < Crop; x++)
            {
                var i = ((((top + y) * resized.Width) + left + x) * 4);
                tensor[0, 0, y, x] = ((px[i + 2] / 255f) - Mean[0]) / Std[0]; // R
                tensor[0, 1, y, x] = ((px[i + 1] / 255f) - Mean[1]) / Std[1]; // G
                tensor[0, 2, y, x] = ((px[i] / 255f) - Mean[2]) / Std[2];     // B
            }
        }

        return tensor;
    }

    internal static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * v;
        }

        var norm = (float)Math.Sqrt(sum);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    public void Dispose() => session.Dispose();
}
