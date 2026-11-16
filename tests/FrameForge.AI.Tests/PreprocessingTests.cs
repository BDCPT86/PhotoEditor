using FrameForge.Analysis;

namespace FrameForge.AI.Tests;

public sealed class PreprocessingTests
{
    [Fact]
    public void Resize_OfUniformImage_StaysUniform_AndHasRequestedSize()
    {
        var image = AnalysisImage.Create(37, 23, (_, _) => (10, 120, 250));

        var resized = ImageOps.Resize(image, 100, 60);

        Assert.Equal((100, 60), (resized.Width, resized.Height));
        for (var i = 0; i < resized.Bgra.Length; i += 4)
        {
            Assert.Equal((250, 120, 10), (resized.Bgra[i], resized.Bgra[i + 1], resized.Bgra[i + 2]));
        }
    }

    [Fact]
    public void FitLongest_PreservesAspect()
    {
        var resized = ImageOps.FitLongest(AnalysisImage.Create(400, 300, (_, _) => (0, 0, 0)), 640);
        Assert.Equal((640, 480), (resized.Width, resized.Height));
    }

    [Fact]
    public void DinoPreprocess_Is224Square_RgbOrder_ImageNetNormalised()
    {
        // Pure red, any aspect ratio.
        var tensor = DinoV2Embedder.Preprocess(AnalysisImage.Create(300, 200, (_, _) => (255, 0, 0)));

        Assert.Equal([1, 3, 224, 224], tensor.Dimensions.ToArray());
        Assert.Equal((1 - 0.485f) / 0.229f, tensor[0, 0, 100, 100], 3); // R channel first
        Assert.Equal((0 - 0.456f) / 0.224f, tensor[0, 1, 100, 100], 3);
        Assert.Equal((0 - 0.406f) / 0.225f, tensor[0, 2, 100, 100], 3);
    }

    [Fact]
    public void DinoPreprocess_CentreCrops()
    {
        // Wide image: left third black, middle white, right third black. The centre crop must be all white.
        var tensor = DinoV2Embedder.Preprocess(AnalysisImage.Create(900, 300, (x, _) => x is >= 300 and < 600 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0)));

        Assert.Equal((1 - 0.485f) / 0.229f, tensor[0, 0, 112, 0], 2);
        Assert.Equal((1 - 0.485f) / 0.229f, tensor[0, 0, 112, 223], 2);
    }

    [Fact]
    public void Normalize_ProducesUnitLength_AndLeavesZeroVectorAlone()
    {
        var v = DinoV2Embedder.Normalize([3, 4]);
        Assert.Equal(0.6f, v[0], 5);
        Assert.Equal(0.8f, v[1], 5);
        Assert.Equal([0f, 0f], DinoV2Embedder.Normalize([0, 0]));
    }
}
