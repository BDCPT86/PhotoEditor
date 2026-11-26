using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Editing;
using FrameForge.Core.Decisions;
using FrameForge.Raw;

namespace FrameForge.Imaging.Tests;

/// <summary>
/// Edited JPEGs of RAW files carry the camera details read from the RAW file's header (RawExif, ADR 0020). The tests
/// on real files run when FRAMEFORGE_TEST_RAW names the Canon EOS R50 samples' RAW folder (with a sibling JPG folder).
/// </summary>
public sealed class RawExportMetadataTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly WicPhotoMetadataReader _wic = new();

    public void Dispose() => _temp.Dispose();

    private static string? RawDir() =>
        Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_RAW") is { } dir && Directory.Exists(dir) ? dir : null;

    private static AnalysisImage SmallImage() => AnalysisImage.Create(40, 30, (x, _) => ((byte)(x * 6), 100, 50));

    [Fact]
    public void RawDetails_WrittenAsExif_ReadBackLikeACameraJpeg()
    {
        var info = new RawPhotoInfo("Canon", "EOS R50", "RF-S18-45mm F4.5-6.3 IS STM", 6000, 4000, 8,
            Iso: 400, ExposureSeconds: 1 / 500.0, FNumber: 5.6, FocalLengthMm: 45, new DateTime(2026, 11, 14, 9, 30, 15));
        var output = _temp.File("raw-exif.jpg");
        using (var stream = File.Create(output))
        {
            var image = SmallImage();
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null, image.Bgra, image.Width * 4);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, RawExif.Create(info), null));
            encoder.Save(stream);
        }

        var read = _wic.Read(output);
        Assert.Equal("Canon", read.CameraMake);
        Assert.Equal("Canon EOS R50", read.CameraModel);
        Assert.Equal("RF-S18-45mm F4.5-6.3 IS STM", read.LensModel);
        Assert.Equal(new DateTime(2026, 11, 14, 9, 30, 15), read.CaptureLocalTime);
        Assert.Equal(400, read.Iso);
        Assert.Equal("1/500", read.ExposureTime);
        Assert.Equal(5.6, read.FNumber);
        Assert.Equal(45, read.FocalLengthMm);
        Assert.Equal(1, read.Orientation);
    }

    [Theory]
    [InlineData(1 / 250.0, 1u, 250u)]
    [InlineData(0.0003125, 1u, 3200u)]
    [InlineData(2.5, 25u, 10u)]
    [InlineData(30.0, 300u, 10u)]
    public void Exposure_IsWrittenAsAnExifRational(double seconds, uint numerator, uint denominator) =>
        Assert.Equal(((ulong)denominator << 32) | numerator, RawExif.ExposureRational(seconds));

    [Fact]
    public void MissingValues_AreLeftOut()
    {
        var info = new RawPhotoInfo("", "", null, 0, 0, 1, null, null, null, null, null);
        var metadata = RawExif.Create(info);

        Assert.False(metadata.ContainsQuery(WicPhotoMetadataReader.MakeQuery));
        Assert.False(metadata.ContainsQuery(WicPhotoMetadataReader.LensModelQuery));
        Assert.False(metadata.ContainsQuery(WicPhotoMetadataReader.DateTimeOriginalQuery));
        Assert.False(metadata.ContainsQuery(WicPhotoMetadataReader.ExposureTimeQuery));
    }

    [Fact]
    public void UnreadableRawSource_StillWritesTheImage_WithRatingAndLabel_ReportingNoMetadata()
    {
        var fake = _temp.File("IMG_0001.CR3");
        File.WriteAllText(fake, "not a raw file");
        var output = _temp.File("out.jpg");

        bool kept;
        using (var stream = File.Create(output))
        {
            kept = new WicJpegEncoder().Write(SmallImage(), new JpegMetadata { SourcePath = fake, Decision = new PhotoDecision(CullState.Keep, 4, ColourLabel.Red) }, stream, 90);
        }

        Assert.False(kept);
        Assert.Equal(4, ReadRating(output));
    }

    [Fact]
    public void RealR50Files_EditedJpeg_HasTheCameraDetails_LikeTheCamerasOwnJpeg()
    {
        var dir = RawDir();
        var jpegDir = dir is null ? null : Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\', '/'))!, "JPG");
        Assert.SkipWhen(dir is null || !Directory.Exists(jpegDir), "Set FRAMEFORGE_TEST_RAW to the R50 samples' RAW folder (with a sibling JPG folder) to run this test.");

        var raws = new RawPhotoMetadataReader(new RawReader());
        var cameraJpegs = Directory.EnumerateFiles(jpegDir!, "*.jpg").Select(f => _wic.Read(f)).ToList();
        var decision = new PhotoDecision(CullState.Keep, 5, ColourLabel.Green);

        foreach (var file in Directory.EnumerateFiles(dir!, "*.cr3"))
        {
            var before = File.ReadAllBytes(file);
            var output = _temp.File(Path.GetFileNameWithoutExtension(file) + ".jpg");
            var image = new WicAnalysisImageLoader().Load(file, 512);
            bool kept;
            using (var stream = File.Create(output))
            {
                kept = new WicJpegEncoder().Write(image, new JpegMetadata { SourcePath = file, RemoveLocation = true, Decision = decision }, stream, 90);
            }

            Assert.True(kept);
            Assert.Equal(before, File.ReadAllBytes(file));

            var source = raws.Read(file);
            var written = _wic.Read(output);
            Assert.Equal(source.CameraMake, written.CameraMake);
            Assert.Equal(source.CameraModel, written.CameraModel);
            Assert.Equal(source.LensModel, written.LensModel);
            Assert.Equal(source.CaptureLocalTime, written.CaptureLocalTime);
            Assert.Equal(source.Iso, written.Iso);
            Assert.Equal(source.ExposureTime, written.ExposureTime);
            Assert.Equal(source.FNumber!.Value, written.FNumber!.Value, 1);
            Assert.Equal(source.FocalLengthMm!.Value, written.FocalLengthMm!.Value, 1);
            Assert.Equal(1, written.Orientation);
            Assert.Equal(5, ReadRating(output));

            // The camera's own JPEG of the same moment names the camera and settings the same way.
            var camera = Assert.Single(cameraJpegs, j => j.CaptureLocalTime == written.CaptureLocalTime);
            Assert.Equal(camera.CameraModel, written.CameraModel);
            Assert.Equal(camera.Iso, written.Iso);
            Assert.Equal(camera.ExposureTime, written.ExposureTime);
        }
    }

    private static int ReadRating(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return ((BitmapMetadata)frame.Metadata).Rating;
    }
}
