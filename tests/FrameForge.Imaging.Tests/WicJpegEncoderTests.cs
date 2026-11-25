using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using FrameForge.Application.Editing;
using FrameForge.Application.Photos;
using FrameForge.Core.Decisions;
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
            kept = _encoder.Write(image, Copy(source), stream, 90);
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
            Assert.True(_encoder.Write(image, JpegMetadata.Nothing, stream, 95));
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
            kept = _encoder.Write(image, Copy(notAJpeg), stream, 90);
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
            _encoder.Write(image, Copy(source), stream, 90);
        }

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
    }

    private const string Gps = "/app1/ifd/gps";
    private const string XmpGpsLatitude = "/xmp/exif:GPSLatitude";
    private const string XmpCity = "/xmp/photoshop:City";
    private const string XmpCountry = "/xmp/photoshop:Country";
    private const string XmpTitle = "/xmp/dc:title";
    private const string XmpLabel = "/xmp/xmp:Label";
    private const string IptcCity = "/app13/irb/8bimiptc/iptc/City";
    private const string IptcState = "/app13/irb/8bimiptc/iptc/{str=Province/State}";
    private const string IptcCaption = "/app13/irb/8bimiptc/iptc/Caption";
    private const string ExifColorSpace = "/app1/ifd/exif/{ushort=40961}";

    private static JpegMetadata Copy(string source, bool removeLocation = false, PhotoDecision? decision = null) =>
        new() { SourcePath = source, RemoveLocation = removeLocation, Decision = decision };

    /// <summary>A source with location in EXIF GPS, XMP and IPTC, plus other fields that must survive.</summary>
    private string SourceWithLocation(string? iccProfile = null)
    {
        var path = _temp.File("located.jpg");
        TestJpeg.Write(
            path,
            120,
            80,
            md =>
            {
                md.SetQuery(WicPhotoMetadataReader.MakeQuery, "TestMake");
                md.SetQuery(Gps + "/{ushort=1}", "S");
                md.SetQuery(Gps + "/{ushort=2}", new[] { TestJpeg.Rational(33, 1), TestJpeg.Rational(55, 1), TestJpeg.Rational(0, 1) });
                md.SetQuery(XmpGpsLatitude, "33,55.0S");
                md.SetQuery(XmpCity, "Cape Town");
                md.SetQuery(XmpCountry, "South Africa");
                md.SetQuery(XmpTitle, "Club match");
                md.SetQuery(IptcCity, "Cape Town");
                md.SetQuery(IptcState, "Western Cape");
                md.SetQuery(IptcCaption, "Final round");
                md.SetQuery(ExifColorSpace, (ushort)0xFFFF); // "uncalibrated", as cameras write for Adobe RGB
            },
            iccProfile);
        return path;
    }

    private string Export(string source, JpegMetadata metadata)
    {
        var image = _loader.Load(source, int.MaxValue);
        var output = _temp.File($"out-{Guid.NewGuid():N}.jpg");
        using (var stream = File.Create(output))
        {
            Assert.True(_encoder.Write(image, metadata, stream, 90));
        }

        return output;
    }

    /// <summary>Runs <paramref name="read"/> against the file's metadata while the file is open.</summary>
    private static T ReadMetadata<T>(string path, Func<BitmapMetadata, T> read)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
        return read((BitmapMetadata)frame.Metadata);
    }

    private static string FileText(string path) => System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));

    [Fact]
    public void All_KeepsLocation()
    {
        var source = SourceWithLocation();

        var output = Export(source, Copy(source));

        Assert.True(ReadMetadata(output, m => m.ContainsQuery(Gps) && m.ContainsQuery(XmpCity) && m.ContainsQuery(IptcCity)));
    }

    [Fact]
    public void RemoveLocation_DropsGpsAndPlaceNames_FromExifXmpAndIptc_KeepingTheRest()
    {
        var source = SourceWithLocation();

        var output = Export(source, Copy(source, removeLocation: true));

        ReadMetadata(output, m =>
        {
            Assert.False(m.ContainsQuery(Gps));
            Assert.False(m.ContainsQuery(XmpGpsLatitude));
            Assert.False(m.ContainsQuery(XmpCity));
            Assert.False(m.ContainsQuery(XmpCountry));
            Assert.False(m.ContainsQuery(IptcCity));
            Assert.False(m.ContainsQuery(IptcState));
            Assert.Equal("TestMake", m.GetQuery(WicPhotoMetadataReader.MakeQuery));
            Assert.Equal("Club match", m.GetQuery(XmpTitle)?.ToString());
            Assert.Equal("Final round", m.GetQuery(IptcCaption));
            return true;
        });

        // Belt and braces: the place name appears nowhere in the file.
        Assert.DoesNotContain("Cape Town", FileText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void ColourProfile_IsAppliedOnLoad_AndNotCopied_SoTheExportShowsTheSameColours()
    {
        // A profile that swaps red and blue: a colour-managed decode shows the red half as blue.
        var source = SourceWithLocation(TestIcc.WriteFile(_temp.File("swap.icc"), swapRedAndBlue: true));
        Assert.True(TestJpeg.IsBlueish(PixelAt(_loader.Load(source, int.MaxValue), 5, 5)), "the loader should apply the embedded profile");

        var output = Export(source, Copy(source));

        // The written pixels are already sRGB; carrying the profile over would swap the colours a second time.
        Assert.DoesNotContain("ICC_PROFILE", FileText(output), StringComparison.Ordinal);
        Assert.True(TestJpeg.IsBlueish(PixelAt(_loader.Load(output, int.MaxValue), 5, 5)));
    }

    [Fact]
    public void ColourProfile_WithAnExifColourSpaceTag_TheTagIsSetToSrgb()
    {
        var source = SourceWithLocation(); // EXIF colour space "uncalibrated"
        TestJpeg.InsertIccProfile(source, TestIcc.Build(swapRedAndBlue: true));
        Assert.True(TestJpeg.IsBlueish(PixelAt(_loader.Load(source, int.MaxValue), 5, 5)), "the loader should apply the spliced-in profile");

        var output = Export(source, Copy(source));

        Assert.Equal((ushort)1, ReadMetadata(output, m => m.GetQuery(ExifColorSpace)));
        Assert.DoesNotContain("ICC_PROFILE", FileText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void CameraAdobeRgb_MarkedOnlyByExif_KeepsItsTags_BecauseThePixelsAreNotConverted()
    {
        var source = _temp.File("adobe.jpg");
        TestJpeg.Write(source, 60, 40, md =>
        {
            md.SetQuery(ExifColorSpace, (ushort)0xFFFF);
            md.SetQuery("/app1/ifd/exif/interop/{ushort=1}", "R03"); // DCF: Adobe RGB
        });
        Assert.True(TestJpeg.IsRedish(PixelAt(_loader.Load(source, int.MaxValue), 5, 5)), "no profile: pixels load unchanged");

        var output = Export(source, Copy(source));

        Assert.Equal(((ushort)0xFFFF, "R03"), ReadMetadata(output, m => (m.GetQuery(ExifColorSpace), m.GetQuery("/app1/ifd/exif/interop/{ushort=1}")?.ToString())));
    }

    [Fact]
    public void JpegSegments_FindAnIccProfile_OnlyWhenThereIsOne()
    {
        var plain = SourceWithMetadata(1);
        var profiled = _temp.File("profiled.jpg");
        TestJpeg.Write(profiled, 30, 20, iccProfilePath: TestIcc.WriteFile(_temp.File("plain.icc"), swapRedAndBlue: false));
        var spliced = _temp.File("spliced.jpg");
        File.Copy(plain, spliced);
        TestJpeg.InsertIccProfile(spliced, TestIcc.Build(swapRedAndBlue: false));

        Assert.False(HasIcc(plain));
        Assert.True(HasIcc(profiled));
        Assert.True(HasIcc(spliced));
        Assert.False(HasIcc(null, [0xFF, 0xD8, 0xFF])); // truncated
        Assert.False(HasIcc(null, "not a jpeg"u8.ToArray()));

        static bool HasIcc(string? path, byte[]? bytes = null)
        {
            using var stream = path is null ? new MemoryStream(bytes!) : (Stream)File.OpenRead(path);
            return JpegSegments.HasIccProfile(stream);
        }
    }

    [Fact]
    public void Decision_WritesRatingAndLabel_IntoASourceWithOnlyExif()
    {
        var source = SourceWithMetadata(1);

        var output = Export(source, Copy(source, decision: new PhotoDecision(CullState.Keep, 4, ColourLabel.Green)));

        ReadMetadata(output, m =>
        {
            Assert.Equal(4, m.Rating);
            Assert.Equal("Green", m.GetQuery(XmpLabel)?.ToString());
            Assert.Equal("TestMake", m.GetQuery(WicPhotoMetadataReader.MakeQuery));
            return true;
        });
    }

    [Fact]
    public void Decision_ReplacesTheRatingAndLabelInTheOriginal_AndUnratedClearsThem()
    {
        var source = _temp.File("rated.jpg");
        TestJpeg.Write(source, 60, 40, md =>
        {
            md.Rating = 5;
            md.SetQuery(XmpLabel, "Red");
        });

        var output = Export(source, Copy(source, decision: PhotoDecision.Unset));

        Assert.Equal((0, false), ReadMetadata(output, m => (m.Rating, m.ContainsQuery(XmpLabel))));
    }

    [Fact]
    public void WithoutDecision_TheRatingInTheOriginalIsKept()
    {
        var source = _temp.File("rated.jpg");
        TestJpeg.Write(source, 60, 40, md => md.Rating = 5);

        Assert.Equal(5, ReadMetadata(Export(source, Copy(source)), m => m.Rating));
    }

    [Fact]
    public void UnreadableSource_StillGetsTheRatingAndLabel()
    {
        var notAJpeg = _temp.File("broken.jpg");
        File.WriteAllText(notAJpeg, "not an image");
        var image = FrameForge.Analysis.AnalysisImage.Create(20, 20, (_, _) => (10, 20, 30));
        var output = _temp.File("out.jpg");

        using (var stream = File.Create(output))
        {
            Assert.False(_encoder.Write(image, Copy(notAJpeg, decision: new PhotoDecision(rating: 3, label: ColourLabel.Blue)), stream, 90));
        }

        Assert.Equal((3, "Blue"), ReadMetadata(output, m => (m.Rating, m.GetQuery(XmpLabel)?.ToString())));
    }

    private static System.Windows.Media.Color PixelAt(FrameForge.Analysis.AnalysisImage image, int x, int y)
    {
        var i = ((y * image.Width) + x) * 4;
        return System.Windows.Media.Color.FromRgb(image.Bgra[i + 2], image.Bgra[i + 1], image.Bgra[i]);
    }
}
