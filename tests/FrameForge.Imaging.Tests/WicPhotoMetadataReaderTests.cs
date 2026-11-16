using System.IO;
using FrameForge.Application.Photos;

namespace FrameForge.Imaging.Tests;

public sealed class WicPhotoMetadataReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly WicPhotoMetadataReader _reader = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void ReadsDimensionsAndExifFields()
    {
        var path = _temp.File("exif.jpg");
        TestJpeg.Write(path, 120, 80, md =>
        {
            md.SetQuery(WicPhotoMetadataReader.MakeQuery, "TestMake");
            md.SetQuery(WicPhotoMetadataReader.ModelQuery, "Model X");
            md.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)6);
            md.SetQuery(WicPhotoMetadataReader.DateTimeOriginalQuery, "2026:05:01 14:03:22");
            md.SetQuery(WicPhotoMetadataReader.ExposureTimeQuery, TestJpeg.Rational(1, 250));
            md.SetQuery(WicPhotoMetadataReader.FNumberQuery, TestJpeg.Rational(28, 10));
            md.SetQuery(WicPhotoMetadataReader.IsoQuery, (ushort)400);
            md.SetQuery(WicPhotoMetadataReader.FocalLengthQuery, TestJpeg.Rational(50, 1));
            md.SetQuery(WicPhotoMetadataReader.LensModelQuery, "Test 50mm F1.8");
        });

        var metadata = _reader.Read(path);

        Assert.Equal(120, metadata.Width);
        Assert.Equal(80, metadata.Height);
        Assert.Equal("TestMake", metadata.CameraMake);
        Assert.Equal("Model X", metadata.CameraModel);
        Assert.Equal("Test 50mm F1.8", metadata.LensModel);
        Assert.Equal(6, metadata.Orientation);
        Assert.Equal(new DateTime(2026, 5, 1, 14, 3, 22, DateTimeKind.Unspecified), metadata.CaptureLocalTime);
        Assert.Equal("1/250", metadata.ExposureTime);
        Assert.Equal(2.8, metadata.FNumber);
        Assert.Equal(400, metadata.Iso);
        Assert.Equal(50, metadata.FocalLengthMm);
    }

    [Fact]
    public void JpegWithoutExif_HasDimensionsAndNullFields()
    {
        var path = _temp.File("plain.jpg");
        TestJpeg.Write(path, 64, 48);

        var metadata = _reader.Read(path);

        Assert.Equal(64, metadata.Width);
        Assert.Equal(48, metadata.Height);
        Assert.Null(metadata.CameraMake);
        Assert.Null(metadata.CaptureLocalTime);
        Assert.Null(metadata.Orientation);
        Assert.Null(metadata.ExposureTime);
    }

    [Fact]
    public void InvalidOrientationValue_IsIgnored()
    {
        var path = _temp.File("bad-orientation.jpg");
        TestJpeg.Write(path, 32, 32, md => md.SetQuery(WicPhotoMetadataReader.OrientationQuery, (ushort)42));

        Assert.Null(_reader.Read(path).Orientation);
    }

    [Fact]
    public void RandomBytes_ThrowPhotoReadException()
    {
        var path = _temp.File("garbage.jpg");
        var bytes = new byte[4096];
        new Random(1234).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);

        var ex = Assert.Throws<PhotoReadException>(() => _reader.Read(path));
        Assert.Contains("garbage.jpg", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFile_ThrowsPhotoReadException()
    {
        var path = _temp.File("empty.jpg");
        File.WriteAllBytes(path, []);

        Assert.Throws<PhotoReadException>(() => _reader.Read(path));
    }

    [Fact]
    public void MissingFile_ThrowsFileNotFound_NotPhotoReadException() =>
        Assert.Throws<FileNotFoundException>(() => _reader.Read(_temp.File("nope.jpg")));

    [Fact]
    public void DoesNotModifyTheSourceFile()
    {
        var path = _temp.File("original.jpg");
        TestJpeg.Write(path, 50, 50);
        var before = File.ReadAllBytes(path);
        var modified = File.GetLastWriteTimeUtc(path);

        _reader.Read(path);

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(1u, 250u, "1/250")]
    [InlineData(10u, 2500u, "1/250")]
    [InlineData(1u, 3u, "1/3")]
    [InlineData(5u, 2u, "2.5")]
    [InlineData(30u, 1u, "30")]
    public void FormatsExposureTime(uint numerator, uint denominator, string expected) =>
        Assert.Equal(expected, WicPhotoMetadataReader.FormatExposureTime((numerator, denominator)));

    [Fact]
    public void FormatExposureTime_RejectsZero()
    {
        Assert.Null(WicPhotoMetadataReader.FormatExposureTime((0, 1)));
        Assert.Null(WicPhotoMetadataReader.FormatExposureTime((1, 0)));
        Assert.Null(WicPhotoMetadataReader.FormatExposureTime(null));
    }

    [Theory]
    [InlineData("2026:05:01 14:03:22", true)]
    [InlineData("2026:05:01 14:03:22\0", true)]
    [InlineData("0000:00:00 00:00:00", false)]
    [InlineData("    :  :     :  :  ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParsesExifDates(string? value, bool parses) =>
        Assert.Equal(parses, WicPhotoMetadataReader.ParseExifDate(value) is not null);
}
