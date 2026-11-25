using System.IO;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Photos;

namespace FrameForge.Imaging.Tests;

/// <summary>
/// RAW files decode through their embedded JPEG everywhere: thumbnails, the viewer and analysis (ADR 0020). The tests on
/// real files run when FRAMEFORGE_TEST_RAW names the Canon EOS R50 samples' RAW folder (with a sibling JPG folder).
/// </summary>
public sealed class RawPreviewDecodingTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("frameforge-raw-decode-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private static string? RawDir() =>
        Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_RAW") is { } dir && Directory.Exists(dir) ? dir : null;

    [Fact]
    public void AFakeRawFile_IsAReadError_ForEveryKindOfLoad()
    {
        var fake = Path.Combine(_temp, "IMG_0001.CR3");
        File.WriteAllText(fake, "not a raw file");

        Assert.Throws<PhotoReadException>(() => new WicAnalysisImageLoader().Load(fake, 512));
        Assert.Throws<PhotoReadException>(() => new WicPhotoBitmapLoader().LoadFull(fake, CancellationToken.None));
        using var thumb = new MemoryStream();
        Assert.Throws<PhotoReadException>(() => new WicThumbnailGenerator().Generate(fake, thumb, 320, CancellationToken.None));
    }

    [Fact]
    public void RealR50Files_DecodeUpright_AtEveryUseSize()
    {
        var dir = RawDir();
        Assert.SkipWhen(dir is null, "Set FRAMEFORGE_TEST_RAW to the R50 samples' RAW folder to run this test.");
        var metadata = new RawPhotoMetadataReader(new Raw.RawReader());

        foreach (var file in Directory.EnumerateFiles(dir!, "*.cr3"))
        {
            var portrait = metadata.Read(file).Orientation is 6 or 8;

            using var thumb = new MemoryStream();
            new WicThumbnailGenerator().Generate(file, thumb, 320, CancellationToken.None);
            thumb.Position = 0;
            var thumbnail = BitmapFrame.Create(thumb, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var analysis = new WicAnalysisImageLoader().Load(file, 1024);
            var screen = new WicPhotoBitmapLoader().LoadForDisplay(file, 2048, CancellationToken.None);

            Assert.Equal(320, Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight));
            Assert.Equal(portrait, thumbnail.PixelHeight > thumbnail.PixelWidth);
            Assert.Equal(portrait, analysis.Height > analysis.Width);
            Assert.Equal(2048, Math.Max(screen.PixelWidth, screen.PixelHeight));
        }
    }

    [Fact]
    public void RealR50File_FullSize_Is6000By4000_AndLooksLikeTheCameraJpeg()
    {
        var dir = RawDir();
        var jpegDir = dir is null ? null : Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\', '/'))!, "JPG");
        Assert.SkipWhen(dir is null || !Directory.Exists(jpegDir), "Set FRAMEFORGE_TEST_RAW to the R50 samples' RAW folder (with a sibling JPG folder) to run this test.");

        // Pair one RAW file with its camera JPEG by capture time.
        var reader = new PhotoMetadataReader(new WicPhotoMetadataReader(), new RawPhotoMetadataReader(new Raw.RawReader()));
        var raw = Directory.EnumerateFiles(dir!, "*.cr3").Order(StringComparer.OrdinalIgnoreCase).First();
        var taken = reader.Read(raw).CaptureLocalTime;
        var jpeg = Directory.EnumerateFiles(jpegDir!, "*.jpg").First(f => reader.Read(f).CaptureLocalTime == taken);

        var full = new WicPhotoBitmapLoader().LoadFull(raw, CancellationToken.None);
        Assert.Equal(6000, Math.Max(full.PixelWidth, full.PixelHeight));
        Assert.Equal(4000, Math.Min(full.PixelWidth, full.PixelHeight));

        // Same framing and colour as the camera's own JPEG: mean absolute difference per channel at 256 px.
        var a = new WicAnalysisImageLoader().Load(raw, 256);
        var b = new WicAnalysisImageLoader().Load(jpeg, 256);
        Assert.Equal((b.Width, b.Height), (a.Width, a.Height));
        Assert.True(MeanDifference(a, b) < 6, $"Mean difference {MeanDifference(a, b):0.0} of 255");
    }

    private static double MeanDifference(AnalysisImage a, AnalysisImage b)
    {
        long sum = 0;
        for (var i = 0; i < a.Bgra.Length; i += 4)
        {
            sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        }

        return sum / (a.Bgra.Length / 4 * 3.0);
    }
}
