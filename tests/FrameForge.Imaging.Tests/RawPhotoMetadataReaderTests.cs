using System.IO;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Raw;

namespace FrameForge.Imaging.Tests;

public sealed class RawPhotoMetadataReaderTests
{
    private sealed class FakeRaw(Func<string, RawPhotoInfo> info) : IRawReader
    {
        public RawPhotoInfo ReadInfo(string path) => info(path);

        public RawPreview ReadPreview(string path) => throw new NotSupportedException();
    }

    private static readonly RawPhotoInfo R50 = new(
        "Canon", " Canon EOS R50 ", "RF-S18-45mm F4.5-6.3 IS STM", 6000, 4000, 6,
        Iso: 400, ExposureSeconds: 1.0 / 1000, FNumber: 6.3, FocalLengthMm: 45, new DateTime(2026, 11, 14, 9, 31, 14, DateTimeKind.Unspecified));

    [Fact]
    public void RawDetails_BecomeTheSameMetadataAJpegWouldHave()
    {
        var metadata = new RawPhotoMetadataReader(new FakeRaw(_ => R50)).Read(@"C:\Shoot\IMG_0001.CR3");

        Assert.Equal(6000, metadata.Width);
        Assert.Equal(4000, metadata.Height);
        Assert.Equal(6, metadata.Orientation);
        Assert.Equal("Canon EOS R50", metadata.CameraModel);
        Assert.Equal("RF-S18-45mm F4.5-6.3 IS STM", metadata.LensModel);
        Assert.Equal(400, metadata.Iso);
        Assert.Equal("1/1000", metadata.ExposureTime);
        Assert.Equal(6.3, metadata.FNumber);
        Assert.Equal(new DateTime(2026, 11, 14, 9, 31, 14, DateTimeKind.Unspecified), metadata.CaptureLocalTime);
    }

    [Theory]
    [InlineData(1.0 / 250, "1/250")]
    [InlineData(1.0 / 3, "1/3")]
    [InlineData(2.5, "2.5")]
    [InlineData(30, "30")]
    public void Exposure_IsWrittenLikeExif(double seconds, string expected) =>
        Assert.Equal(expected, RawPhotoMetadataReader.FormatExposure(seconds));

    [Fact]
    public void MissingValues_StayMissing()
    {
        var bare = new RawPhotoInfo("", "", null, 0, 0, 1, null, null, null, null, null);

        var metadata = new RawPhotoMetadataReader(new FakeRaw(_ => bare)).Read(@"C:\x.cr3");

        Assert.Null(metadata.Width);
        Assert.Null(metadata.CameraMake);
        Assert.Null(metadata.ExposureTime);
        Assert.Null(metadata.CaptureLocalTime);
    }

    [Fact]
    public void AnUnreadableRawFile_IsAPhotoReadException_SoImportMarksItUnreadable()
    {
        var reader = new RawPhotoMetadataReader(new FakeRaw(_ => throw new RawReadException("This file can't be read as a RAW photo.")));

        var ex = Assert.Throws<PhotoReadException>(() => reader.Read(@"C:\broken.cr3"));
        Assert.Contains("RAW photo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cr3IsSupported_AndRaw_JpegIsNotRaw()
    {
        Assert.True(SupportedPhotoFormats.IsSupported(@"C:\a\IMG_1.CR3"));
        Assert.True(SupportedPhotoFormats.IsRaw(@"C:\a\IMG_1.cr3"));
        Assert.False(SupportedPhotoFormats.IsRaw(@"C:\a\IMG_1.JPG"));
        Assert.False(SupportedPhotoFormats.IsSupported(@"C:\a\IMG_1.CR2")); // untested formats stay off
    }

    [Fact]
    public void RealR50Pairs_EveryRawFileHasTheCaptureTimeOfOneCameraJpeg()
    {
        var rawDir = Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_RAW");
        var jpegDir = rawDir is null ? null : Path.Combine(Path.GetDirectoryName(rawDir.TrimEnd('\\', '/'))!, "JPG");
        Assert.SkipWhen(rawDir is null || !Directory.Exists(jpegDir), "Set FRAMEFORGE_TEST_RAW to the R50 samples' RAW folder (with a sibling JPG folder) to run this test.");

        var reader = new PhotoMetadataReader(new WicPhotoMetadataReader(), new RawPhotoMetadataReader(new RawReader()));
        var rawTimes = Directory.EnumerateFiles(rawDir!, "*.cr3").Select(f => reader.Read(f)).ToList();
        var jpegTimes = Directory.EnumerateFiles(jpegDir!, "*.jpg").Select(f => reader.Read(f).CaptureLocalTime).ToHashSet();

        Assert.NotEmpty(rawTimes);
        Assert.All(rawTimes, m =>
        {
            Assert.Contains(m.CaptureLocalTime, jpegTimes); // LibRaw's time-zone round trip gives the camera clock back
            Assert.Equal("Canon EOS R50", m.CameraModel);
            Assert.NotNull(m.ExposureTime);
        });
    }
}
