using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Analysis;

public sealed class AnalysisInsightsTests
{
    private static readonly DateTime T0 = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static (Photo Photo, PhotoAnalysis Analysis) Make(string name, double sharpness, ulong hash, double seconds,
        string? content = null, double highlights = 0, double shadows = 0)
    {
        var photo = AnalysisTestPhotos.Create(name);
        photo.CaptureLocalTime = T0.AddSeconds(seconds);
        var analysis = new PhotoAnalysis
        {
            ContentHash = content ?? "unique-" + name,
            PerceptualHash = hash,
            Sharpness = new SharpnessResult(sharpness, sharpness / 2, 4, 1, 2, new double[16], 1024, 683),
            Exposure = new ExposureResult(120, highlights, shadows, new int[16]),
        };
        return (photo, analysis);
    }

    private static IReadOnlyDictionary<Guid, PhotoInsight> Build(IEnumerable<(Photo Photo, PhotoAnalysis Analysis)> items, AnalysisThresholds thresholds)
    {
        var list = items.ToList();
        return AnalysisInsights.Build(list.Select(i => i.Photo).ToList(), list.ToDictionary(i => i.Photo.Id, i => i.Analysis), thresholds, AnalysisSettings.Default);
    }

    [Fact]
    public void BlurAndClippingFlags_FollowThresholds()
    {
        var soft = Make("soft.jpg", sharpness: 40, hash: 0, seconds: 0);
        var crisp = Make("crisp.jpg", sharpness: 400, hash: ulong.MaxValue, seconds: 0, highlights: 3.5, shadows: 0.2);

        var insights = Build([soft, crisp], AnalysisThresholds.Default);

        Assert.True(insights[soft.Photo.Id].LikelyBlurry);
        Assert.False(insights[crisp.Photo.Id].LikelyBlurry);
        Assert.True(insights[crisp.Photo.Id].HighlightsClipped);
        Assert.False(insights[crisp.Photo.Id].ShadowsClipped);
    }

    [Fact]
    public void ChangingThresholds_ChangesFlagsAndGroups_FromTheSameMeasurements()
    {
        var a = Make("a.jpg", 50, 0b0000, 0);
        var b = Make("b.jpg", 90, 0b0111, 4);   // 3 bits from a
        var c = Make("c.jpg", 300, 0b0000, 40); // same as a, but 40 s later

        var strict = Build([a, b, c], AnalysisThresholds.Default with { MaxHashDistance = 2, BurstWindowSeconds = 10, BlurScore = 60 });
        var loose = Build([a, b, c], AnalysisThresholds.Default with { MaxHashDistance = 3, BurstWindowSeconds = 0, BlurScore = 100 });

        Assert.All(strict.Values, i => Assert.Null(i.GroupKey));
        Assert.True(strict[a.Photo.Id].LikelyBlurry);
        Assert.False(strict[b.Photo.Id].LikelyBlurry);

        Assert.Equal(3, loose[a.Photo.Id].GroupSize);
        Assert.Equal(loose[a.Photo.Id].GroupKey, loose[c.Photo.Id].GroupKey);
        Assert.True(loose[b.Photo.Id].LikelyBlurry);
    }

    [Fact]
    public void SharpestInGroup_IsMarked_AndOnlyOnePerGroup()
    {
        var a = Make("a.jpg", 120, 0, 0);
        var b = Make("b.jpg", 480, 1, 1);
        var c = Make("c.jpg", 200, 3, 2);

        var insights = Build([a, b, c], AnalysisThresholds.Default);

        Assert.True(insights[b.Photo.Id].IsSharpestInGroup);
        Assert.Single(insights.Values, i => i.IsSharpestInGroup);
        Assert.Contains(insights[b.Photo.Id].Details, d => d.Value.Contains("sharpest in group", StringComparison.Ordinal));
    }

    [Fact]
    public void ExactDuplicates_AreFoundByContentHash()
    {
        var a = Make("a.jpg", 100, 0, 0, content: "same");
        var b = Make("b.jpg", 100, ulong.MaxValue, 999, content: "same");

        var insights = Build([a, b], AnalysisThresholds.Default);

        Assert.Equal(1, insights[a.Photo.Id].ExactDuplicateCount);
        var line = Assert.Single(insights[b.Photo.Id].Details, d => d.Topic == "Exact duplicate");
        Assert.Contains(a.Photo.FilePath, line.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Explanations_StateValuesThresholdsAndMethod()
    {
        var soft = Make("soft.jpg", 40, 0, 0);
        var near = Make("near.jpg", 80, 0b11, 2);

        var insight = Build([soft, near], AnalysisThresholds.Default)[soft.Photo.Id];

        var sharpness = Assert.Single(insight.Details, d => d.Topic == "Sharpness");
        Assert.True(sharpness.IsFlagged);
        Assert.Contains("40", sharpness.Value, StringComparison.Ordinal);
        Assert.Contains("threshold of 60", sharpness.Explanation, StringComparison.Ordinal);
        Assert.Contains("Laplacian", sharpness.Explanation, StringComparison.Ordinal);

        var similar = Assert.Single(insight.Details, d => d.Topic == "Similar photos");
        Assert.Contains("near.jpg", similar.Value, StringComparison.Ordinal);
        Assert.Contains("2 of 64 bits", similar.Value, StringComparison.Ordinal);
        Assert.Contains("2 s apart", similar.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotosWithoutAnalysis_HaveNoInsight()
    {
        var analysed = Make("a.jpg", 100, 0, 0);
        var notAnalysed = AnalysisTestPhotos.Create("b.jpg");

        var insights = AnalysisInsights.Build(
            [analysed.Photo, notAnalysed],
            new Dictionary<Guid, PhotoAnalysis> { [analysed.Photo.Id] = analysed.Analysis },
            AnalysisThresholds.Default,
            AnalysisSettings.Default);

        Assert.True(insights.ContainsKey(analysed.Photo.Id));
        Assert.False(insights.ContainsKey(notAnalysed.Id));
    }

    [Fact]
    public void CacheKey_RejectsEveryKindOfMismatch()
    {
        var photo = AnalysisTestPhotos.Create("a.jpg");
        var settings = AnalysisSettings.Default;
        var row = AnalysisCache.Create(photo, AnalysisKind.Sharpness, settings, new SharpnessResult(1, 1, 4, 0, 0, new double[16], 10, 10), DateTime.UtcNow);

        Assert.True(AnalysisCache.IsValid(row, photo, settings));
        Assert.False(AnalysisCache.IsValid(row with { AlgorithmVersion = "v0" }, photo, settings));
        Assert.False(AnalysisCache.IsValid(row, photo, settings with { SharpnessGrid = 3 }));
        Assert.False(AnalysisCache.IsValid(row with { SourceSizeBytes = 999 }, photo, settings));
        Assert.False(AnalysisCache.IsValid(row with { SourceModifiedUtc = DateTime.UnixEpoch }, photo, settings));
        Assert.False(AnalysisCache.IsValid(row with { PhotoId = Guid.NewGuid() }, photo, settings));
    }

    [Fact]
    public void CorruptStoredJson_IsTreatedAsMissing()
    {
        var photo = AnalysisTestPhotos.Create("a.jpg");
        var row = AnalysisCache.Create(photo, AnalysisKind.Exposure, AnalysisSettings.Default, new ExposureResult(1, 2, 3, new int[16]), DateTime.UtcNow)
            with { ValueJson = "{not json" };

        Assert.Null(AnalysisCache.Apply(PhotoAnalysis.Empty, row).Exposure);
    }
}
