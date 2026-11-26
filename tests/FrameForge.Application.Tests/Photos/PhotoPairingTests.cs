using System.Diagnostics;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Photos;

public sealed class PhotoPairingTests
{
    private const string Shoot = @"C:\Photos\Shoot";
    private static readonly DateTime Taken = new(2026, 11, 14, 9, 30, 15);

    private static Photo Make(string fileName, string folder = Shoot, DateTime? taken = null, ImportStatus status = ImportStatus.Imported, bool noTime = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            FilePath = Path.Combine(folder, fileName),
            FileName = fileName,
            FolderPath = folder,
            CaptureLocalTime = noTime ? null : taken ?? Taken,
            Status = status,
        };

    [Fact]
    public void RawAndJpeg_WithTheSameName_Pair_WithTheJpegAsCover()
    {
        var jpeg = Make("IMG_0603.JPG");
        var raw = Make("IMG_0603.CR3");

        var pairs = PhotoPairing.Find([raw, jpeg]);

        var pair = Assert.Single(pairs.Pairs);
        Assert.Same(jpeg, pair.Cover);
        Assert.Same(raw, pair.Raw);
        Assert.Same(pair, pairs.ByCover(jpeg.Id));
        Assert.True(pairs.IsHiddenRaw(raw.Id));
        Assert.False(pairs.IsHiddenRaw(jpeg.Id));
        Assert.Null(pairs.ByCover(raw.Id));
    }

    [Theory]
    [InlineData("img_0603.jpeg", "IMG_0603.cr3")]
    [InlineData("IMG_0603.Jpg", "img_0603.CR3")]
    public void NamesAndExtensions_MatchIgnoringCase(string jpegName, string rawName) =>
        Assert.Single(PhotoPairing.Find([Make(jpegName), Make(rawName)]).Pairs);

    [Fact]
    public void FolderPaths_MatchIgnoringCaseAndATrailingSeparator() =>
        Assert.Single(PhotoPairing.Find([Make("IMG_1.JPG", @"C:\Photos\Shoot"), Make("IMG_1.CR3", @"c:\photos\shoot\")]).Pairs);

    [Fact]
    public void DifferentFolders_DoNotPair() =>
        Assert.Empty(PhotoPairing.Find([Make("IMG_1.JPG", @"C:\Photos\A"), Make("IMG_1.CR3", @"C:\Photos\B")]).Pairs);

    [Fact]
    public void DifferentNames_DoNotPair() =>
        Assert.Empty(PhotoPairing.Find([Make("IMG_1.JPG"), Make("IMG_2.CR3")]).Pairs);

    [Fact]
    public void TwoJpegs_WithTheName_MakeItAmbiguous_SoNothingPairs() =>
        Assert.Empty(PhotoPairing.Find([Make("IMG_1.JPG"), Make("IMG_1.JPEG"), Make("IMG_1.CR3")]).Pairs);

    [Fact]
    public void JpegsAlone_OrRawAlone_DoNotPair() =>
        Assert.Empty(PhotoPairing.Find([Make("IMG_1.JPG"), Make("IMG_2.JPG"), Make("IMG_3.CR3")]).Pairs);

    [Fact]
    public void CaptureTimes_MustMatchToTheSecond()
    {
        var sameSecond = PhotoPairing.Find([Make("A.JPG", taken: Taken), Make("A.CR3", taken: Taken.AddMilliseconds(400))]);
        var otherDay = PhotoPairing.Find([Make("B.JPG", taken: Taken), Make("B.CR3", taken: Taken.AddDays(3))]);
        var oneSecondApart = PhotoPairing.Find([Make("C.JPG", taken: Taken), Make("C.CR3", taken: Taken.AddSeconds(1))]);

        Assert.Single(sameSecond.Pairs);
        Assert.Empty(otherDay.Pairs);
        Assert.Empty(oneSecondApart.Pairs);
    }

    [Fact]
    public void AMissingCaptureTime_OnEitherSide_DoesNotPreventAPair() =>
        Assert.Single(PhotoPairing.Find([Make("A.JPG", noTime: true), Make("A.CR3")]).Pairs);

    [Theory]
    [InlineData(ImportStatus.Missing, ImportStatus.Imported)]
    [InlineData(ImportStatus.Imported, ImportStatus.Failed)]
    public void AMissingOrUnreadableFile_IsShownOnItsOwn(ImportStatus jpegStatus, ImportStatus rawStatus) =>
        Assert.Empty(PhotoPairing.Find([Make("A.JPG", status: jpegStatus), Make("A.CR3", status: rawStatus)]).Pairs);

    [Fact]
    public void ManyPhotos_PairInWellUnder100Ms()
    {
        // 5,000 RAW + JPEG frames = 10,000 photos, the brief's size for the load-time budget.
        var photos = new List<Photo>(10_000);
        for (var i = 0; i < 5_000; i++)
        {
            var folder = $@"C:\Photos\Day{i / 500}";
            var taken = Taken.AddSeconds(i);
            photos.Add(Make($"IMG_{i:D4}.JPG", folder, taken));
            photos.Add(Make($"IMG_{i:D4}.CR3", folder, taken));
        }

        PhotoPairing.Find(photos); // warm up
        var stopwatch = Stopwatch.StartNew();
        var pairs = PhotoPairing.Find(photos);
        stopwatch.Stop();

        Assert.Equal(5_000, pairs.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void NoPairs_IsTheSharedEmptyResult() =>
        Assert.Same(PhotoPairs.None, PhotoPairing.Find([Make("A.JPG")]));
}
