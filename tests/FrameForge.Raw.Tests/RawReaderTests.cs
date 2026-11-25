namespace FrameForge.Raw.Tests;

/// <summary>
/// LibRaw loading and the reader. Tests on real RAW files run only when FRAMEFORGE_TEST_RAW names a folder of Canon EOS
/// R50 .CR3 files (they are the user's photos: never in the repository, only read).
/// </summary>
public sealed class RawReaderTests : IDisposable
{
    public const string RawVariable = "FRAMEFORGE_TEST_RAW";

    /// <summary>Landscape, and portrait either way: what the R50 samples use.</summary>
    private static readonly int[] UprightOrientations = [1, 6, 8];

    private readonly string _temp = Directory.CreateTempSubdirectory("frameforge-raw-tests-").FullName;
    private readonly RawReader _reader = new();

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string? FirstRawFile()
    {
        var dir = Environment.GetEnvironmentVariable(RawVariable);
        return string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)
            ? null
            : Directory.EnumerateFiles(dir, "*.cr3").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    [Fact]
    public void TheBundledLibrary_Loads_AsTheTestedBuild()
    {
        var status = LibRawLibrary.Status;

        Assert.True(status.IsReady, status.Message);
        Assert.StartsWith(LibRawLibrary.TestedVersion, status.Version, StringComparison.Ordinal);
        Assert.True(status.IsTestedBuild);
    }

    [Fact]
    public void AMissingLibrary_OrOneThatIsNotADll_TurnsRawOff_WithAReason()
    {
        var missing = LibRawLibrary.Load(Path.Combine(_temp, "libraw.dll"));
        Assert.Equal(LibRawState.NotFound, missing.State);
        Assert.Contains("missing", missing.Message, StringComparison.Ordinal);

        var notADll = Path.Combine(_temp, "fake", "libraw.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(notADll)!);
        File.WriteAllText(notADll, "not a library");
        var broken = LibRawLibrary.Load(notADll);
        Assert.Equal(LibRawState.CannotLoad, broken.State);
        Assert.Contains("Visual C++", broken.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 8)]
    [InlineData(6, 6)]
    [InlineData(7, 1)]
    public void LibRawsFlip_BecomesTheExifOrientation(int flip, int exif) =>
        Assert.Equal(exif, RawReader.ExifOrientationFromFlip(flip));

    [Fact]
    public void AFileThatIsNotRaw_IsAReadError_AndAMissingOneIsNotFound()
    {
        var notRaw = Path.Combine(_temp, "fake.cr3");
        File.WriteAllText(notRaw, "this is not a raw file");

        Assert.Throws<RawReadException>(() => _reader.ReadInfo(notRaw));
        Assert.Throws<RawReadException>(() => _reader.ReadPreview(notRaw));
        Assert.Throws<FileNotFoundException>(() => _reader.ReadInfo(Path.Combine(_temp, "gone.cr3")));
    }

    [Fact]
    public void RealR50File_HasItsDetails_AndAFullSizeJpegPreview()
    {
        var path = FirstRawFile();
        Assert.SkipWhen(path is null, $"Set {RawVariable} to a folder of Canon EOS R50 .CR3 files to run this test.");

        var info = _reader.ReadInfo(path);
        var preview = _reader.ReadPreview(path);

        Assert.Equal("Canon", info.CameraMake);
        Assert.Contains("R50", info.CameraModel, StringComparison.Ordinal);
        Assert.Equal(6000, Math.Max(info.Width, info.Height));
        Assert.Equal(4000, Math.Min(info.Width, info.Height));
        Assert.Contains(info.ExifOrientation, UprightOrientations);
        Assert.NotNull(info.CaptureLocalTime);
        Assert.NotNull(info.Iso);
        Assert.NotNull(info.ExposureSeconds);
        Assert.Equal(info.ExifOrientation, preview.ExifOrientation);
        Assert.True(preview.Jpeg.Length > 500_000, $"Preview is only {preview.Jpeg.Length} bytes");
        Assert.Equal([0xFF, 0xD8], preview.Jpeg[..2]); // a JPEG
    }

    [Fact]
    public void DamagedCopiesOfARealFile_AreReadErrors_NotCrashes()
    {
        var path = FirstRawFile();
        Assert.SkipWhen(path is null, $"Set {RawVariable} to a folder of Canon EOS R50 .CR3 files to run this test.");
        var original = File.ReadAllBytes(path);

        // As in the M07 spike: a cut-off file and a zeroed header. Copies only; the original is just read.
        var truncated = Path.Combine(_temp, "truncated.cr3");
        File.WriteAllBytes(truncated, original[..(original.Length / 100)]);
        var zeroed = Path.Combine(_temp, "zeroed.cr3");
        var header = (byte[])original.Clone();
        Array.Clear(header, 0, 4096);
        File.WriteAllBytes(zeroed, header);

        Assert.Throws<RawReadException>(() => _reader.ReadInfo(zeroed));
        Assert.Throws<RawReadException>(() => _reader.ReadPreview(zeroed));
        var exception = Record.Exception(() => _reader.ReadPreview(truncated));
        Assert.True(exception is null or RawReadException, $"Unexpected {exception?.GetType().Name}");
    }
}
