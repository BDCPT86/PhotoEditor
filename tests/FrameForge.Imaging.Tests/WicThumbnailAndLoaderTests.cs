using System.IO;
using System.Windows.Media.Imaging;
using FrameForge.Application.Photos;

namespace FrameForge.Imaging.Tests;

public sealed class WicThumbnailAndLoaderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly WicThumbnailGenerator _generator = new();
    private readonly WicPhotoBitmapLoader _loader = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private BitmapSource GenerateThumbnail(string source, int maxSize)
    {
        var target = _temp.File(Guid.NewGuid().ToString("N") + ".thumb.jpg");
        using (var stream = File.Create(target))
        {
            _generator.Generate(source, stream, maxSize, Ct);
        }

        return _loader.LoadThumbnail(target);
    }

    [Fact]
    public void Thumbnail_LongestSideIsBounded_AndAspectPreserved()
    {
        var source = _temp.File("landscape.jpg");
        TestJpeg.Write(source, 1200, 800);

        var thumb = GenerateThumbnail(source, 320);

        Assert.Equal(320, thumb.PixelWidth);
        Assert.Equal(213, thumb.PixelHeight, tolerance: 1);
    }

    [Fact]
    public void Thumbnail_DoesNotUpscaleSmallImages()
    {
        var source = _temp.File("small.jpg");
        TestJpeg.Write(source, 200, 100);

        var thumb = GenerateThumbnail(source, 320);

        Assert.Equal(200, thumb.PixelWidth);
        Assert.Equal(100, thumb.PixelHeight);
    }

    [Fact]
    public void Thumbnail_AppliesExifOrientation()
    {
        var source = _temp.File("rotated.jpg");
        TestJpeg.Write(source, 1200, 800, md => md.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)6));

        var thumb = GenerateThumbnail(source, 320);

        // Stored landscape, displayed portrait.
        Assert.Equal(320, thumb.PixelHeight);
        Assert.Equal(213, thumb.PixelWidth, tolerance: 1);

        // Rotating 90° clockwise moves the red left half to the top.
        Assert.True(TestJpeg.IsRedish(TestJpeg.SamplePixel(thumb, thumb.PixelWidth / 2, 10)));
        Assert.True(TestJpeg.IsBlueish(TestJpeg.SamplePixel(thumb, thumb.PixelWidth / 2, thumb.PixelHeight - 10)));
    }

    [Fact]
    public void Thumbnail_OfCorruptFile_ThrowsPhotoReadException()
    {
        var source = _temp.File("corrupt.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02]);

        using var output = new MemoryStream();
        Assert.Throws<PhotoReadException>(() => _generator.Generate(source, output, 320, Ct));
    }

    [Fact]
    public void Thumbnail_OfMissingFile_ThrowsFileNotFound()
    {
        using var output = new MemoryStream();
        Assert.Throws<FileNotFoundException>(() => _generator.Generate(_temp.File("gone.jpg"), output, 320, Ct));
    }

    [Theory]
    [InlineData(1, 300, 200)]
    [InlineData(3, 300, 200)]
    [InlineData(6, 200, 300)]
    [InlineData(8, 200, 300)]
    public void LoadFull_ReturnsFrozenFullResolution_WithOrientation(int orientation, int expectedWidth, int expectedHeight)
    {
        var source = _temp.File($"full-{orientation}.jpg");
        TestJpeg.Write(source, 300, 200, md => md.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)orientation));

        var bitmap = _loader.LoadFull(source, Ct);

        Assert.True(bitmap.IsFrozen);
        Assert.Equal(expectedWidth, bitmap.PixelWidth);
        Assert.Equal(expectedHeight, bitmap.PixelHeight);
    }

    [Fact]
    public void LoadFull_HonoursPriorCancellation()
    {
        var source = _temp.File("cancel.jpg");
        TestJpeg.Write(source, 64, 64);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _loader.LoadFull(source, cts.Token));
    }

    [Fact]
    public void AnalysisLoader_DownscalesOrientsAndReturnsBgra()
    {
        var source = _temp.File("analysis.jpg");
        TestJpeg.Write(source, 1200, 800, md => md.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)6));

        var image = new WicAnalysisImageLoader().Load(source, 512);

        Assert.Equal(341, image.Width, tolerance: 1);   // rotated to portrait, longest side 512
        Assert.Equal(512, image.Height);
        Assert.Equal(image.Width * image.Height * 4, image.Bgra.Length);
        // Rotating 90° clockwise puts the red left half on top.
        var top = (image.Width / 2 * 4) + (10 * image.Width * 4);
        Assert.True(image.Bgra[top + 2] > 150 && image.Bgra[top] < 100);
    }

    [Fact]
    public void AnalysisLoader_RejectsCorruptFiles()
    {
        var source = _temp.File("corrupt-analysis.jpg");
        File.WriteAllBytes(source, [1, 2, 3, 4]);

        Assert.Throws<PhotoReadException>(() => new WicAnalysisImageLoader().Load(source, 512));
    }

    [Fact]
    public void LoadFull_ReleasesFileHandle()
    {
        var source = _temp.File("handle.jpg");
        TestJpeg.Write(source, 64, 64);

        _loader.LoadFull(source, Ct);

        File.Delete(source); // would throw if the decoder kept the file open
        Assert.False(File.Exists(source));
    }
}
