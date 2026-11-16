using System.IO;
using System.Security.Cryptography;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Imaging.Tests;

public sealed class WicJpegEncoderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly WicJpegEncoder _encoder = new();
    private readonly WicAnalysisImageLoader _loader = new();
    private readonly WicPhotoMetadataReader _metadata = new();

    public void Dispose() => _temp.Dispose();

    private string SourceWithMetadata(ushort orientation)
    {
        var path = _temp.File("source.jpg");
        TestJpeg.Write(path, 300, 200, md =>
        {
            md.SetQuery(WicPhotoMetadataReader.OrientationQuery, orientation);
            md.SetQuery(WicPhotoMetadataReader.MakeQuery, "TestMake");
            md.SetQuery(WicPhotoMetadataReader.ModelQuery, "TestModel");
            md.SetQuery(WicPhotoMetadataReader.IsoQuery, (ushort)400);
        });
        return path;
    }

    [Fact]
    public void EditedPhoto_KeepsExif_ResetsOrientation_AndIsUpright()
    {
        // Stored landscape with orientation 6 (displayed rotated 90° clockwise → portrait).
        var source = SourceWithMetadata(6);
        var image = EditRenderer.Render(_loader.Load(source, int.MaxValue), EditRecipe.Neutral with { Exposure = 0.3 }, null, TestContext.Current.CancellationToken);
        var output = _temp.File("out.jpg");

        bool kept;
        using (var stream = File.Create(output))
        {
            kept = _encoder.Write(image, source, stream, 90);
        }

        Assert.True(kept);
        var written = _metadata.Read(output);
        Assert.Equal("TestMake", written.CameraMake);
        Assert.Equal("TestModel", written.CameraModel);
        Assert.Equal(400, written.Iso);
        Assert.True(written.Orientation is null or 1, $"orientation {written.Orientation}");

        // Viewers will show it exactly as rendered: portrait, 200×300.
        var reloaded = _loader.Load(output, int.MaxValue);
        Assert.Equal((200, 300), (reloaded.Width, reloaded.Height));
    }

    [Fact]
    public void WithoutMetadataSource_WritesAPlainJpeg()
    {
        var image = FrameForge.Analysis.AnalysisImage.Create(40, 30, (x, _) => ((byte)(x * 6), 100, 50));
        var output = _temp.File("plain.jpg");
        using (var stream = File.Create(output))
        {
            Assert.True(_encoder.Write(image, null, stream, 95));
        }

        var reloaded = _loader.Load(output, int.MaxValue);
        Assert.Equal((40, 30), (reloaded.Width, reloaded.Height));
    }

    [Fact]
    public void UnreadableMetadataSource_StillWritesTheImage_ReportingNoMetadata()
    {
        var notAJpeg = _temp.File("broken.jpg");
        File.WriteAllText(notAJpeg, "not an image");
        var image = FrameForge.Analysis.AnalysisImage.Create(20, 20, (_, _) => (10, 20, 30));
        var output = _temp.File("out.jpg");

        bool kept;
        using (var stream = File.Create(output))
        {
            kept = _encoder.Write(image, notAJpeg, stream, 90);
        }

        Assert.False(kept);
        Assert.Equal((20, 20), (_loader.Load(output, int.MaxValue).Width, 20));
    }

    [Fact]
    public void Encoding_NeverModifiesTheOriginal()
    {
        var source = SourceWithMetadata(1);
        var before = SHA256.HashData(File.ReadAllBytes(source));
        var image = EditRenderer.Render(_loader.Load(source, int.MaxValue), EditRecipe.Neutral with { Saturation = 50, Crop = new CropRect(0.1, 0.1, 0.5, 0.5) }, null, TestContext.Current.CancellationToken);

        using (var stream = File.Create(_temp.File("out.jpg")))
        {
            _encoder.Write(image, source, stream, 90);
        }

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
    }
}
