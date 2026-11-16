namespace FrameForge.AI.Tests;

public sealed class YuNetDecoderTests
{
    private const int Input = 64;

    /// <summary>Empty outputs for a 64×64 input: 8×8, 4×4 and 2×2 anchors.</summary>
    private static List<YuNetDecoder.StrideOutput> Empty() => YuNetDecoder.Strides.Select(s =>
    {
        var n = (Input / s) * (Input / s);
        return new YuNetDecoder.StrideOutput(s, new float[n], new float[n], new float[n * 4], new float[n * 10]);
    }).ToList();

    private static void SetAnchor(YuNetDecoder.StrideOutput o, int row, int col, float cls, float obj, float dx, float dy, float logW, float logH)
    {
        var idx = (row * (Input / o.Stride)) + col;
        o.Cls[idx] = cls;
        o.Obj[idx] = obj;
        o.Bbox[(idx * 4) + 0] = dx;
        o.Bbox[(idx * 4) + 1] = dy;
        o.Bbox[(idx * 4) + 2] = logW;
        o.Bbox[(idx * 4) + 3] = logH;
        for (var k = 0; k < 5; k++)
        {
            o.Kps[(idx * 10) + (2 * k)] = 0.5f;
            o.Kps[(idx * 10) + (2 * k) + 1] = 0.5f;
        }
    }

    [Fact]
    public void DecodesAnchorGeometry_ScoreAndLandmarks()
    {
        var outputs = Empty();
        // Stride 8, row 2, col 3: centre ((3 + 0.5)·8, (2 + 0.5)·8) = (28, 20); size e^ln2 · 8 = 16.
        SetAnchor(outputs[0], 2, 3, 0.81f, 0.64f, 0.5f, 0.5f, MathF.Log(2), MathF.Log(2));

        var face = Assert.Single(YuNetDecoder.Decode(outputs, Input, 64, 64, 0.5f));

        Assert.Equal(20f / 64, face.X, 4);
        Assert.Equal(12f / 64, face.Y, 4);
        Assert.Equal(16f / 64, face.Width, 4);
        Assert.Equal(16f / 64, face.Height, 4);
        Assert.Equal(MathF.Sqrt(0.81f * 0.64f), face.Score, 4);
        Assert.Equal(28f / 64, face.Landmarks[0], 4); // (0.5 + 3) · 8
        Assert.Equal(20f / 64, face.Landmarks[1], 4); // (0.5 + 2) · 8
    }

    [Fact]
    public void ScoreBelowThreshold_IsDropped()
    {
        var outputs = Empty();
        SetAnchor(outputs[0], 1, 1, 0.3f, 0.3f, 0, 0, 0, 0);
        Assert.Empty(YuNetDecoder.Decode(outputs, Input, 64, 64, 0.5f));
    }

    [Fact]
    public void Nms_KeepsHighestOfOverlappingBoxes_AndSeparateFaces()
    {
        var outputs = Empty();
        SetAnchor(outputs[0], 2, 3, 0.9f, 0.9f, 0.5f, 0.5f, MathF.Log(2), MathF.Log(2));   // best
        SetAnchor(outputs[0], 2, 4, 0.8f, 0.8f, 0.0f, 0.5f, MathF.Log(2), MathF.Log(2));   // 4 px right of the best: overlaps
        SetAnchor(outputs[1], 3, 3, 0.7f, 0.7f, 0.5f, 0.5f, 0, 0);                          // far away (56, 56)

        var faces = YuNetDecoder.Decode(outputs, Input, 64, 64, 0.5f);

        Assert.Equal(2, faces.Count);
        Assert.Equal(0.9f, faces[0].Score, 4);
        Assert.Equal(0.7f, faces[1].Score, 4);
    }

    [Fact]
    public void BoxesAreClampedToTheUnpaddedImage()
    {
        var outputs = Empty();
        SetAnchor(outputs[2], 1, 1, 0.9f, 0.9f, 0.5f, 0.5f, MathF.Log(2), MathF.Log(2)); // centre (48, 48), 64×64 box

        var face = Assert.Single(YuNetDecoder.Decode(outputs, Input, 50, 40, 0.5f));

        Assert.InRange(face.X + face.Width, 0, 1.0001f);
        Assert.InRange(face.Y + face.Height, 0, 1.0001f);
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 0, 0, 10, 10, 1)]
    [InlineData(0, 0, 10, 10, 20, 20, 30, 30, 0)]
    [InlineData(0, 0, 10, 10, 5, 0, 15, 10, 50f / 150)]
    public void Iou_IsIntersectionOverUnion(float ax1, float ay1, float ax2, float ay2, float bx1, float by1, float bx2, float by2, float expected)
    {
        Assert.Equal(expected, YuNetDecoder.Iou(ax1, ay1, ax2, ay2, bx1, by1, bx2, by2), 4);
    }
}
