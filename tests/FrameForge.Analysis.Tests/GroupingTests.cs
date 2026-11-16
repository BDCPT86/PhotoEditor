namespace FrameForge.Analysis.Tests;

public sealed class GroupingTests
{
    private static readonly DateTime T0 = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static SimilarityItem Item(int n, ulong hash, double? seconds) =>
        new(new Guid(n, 0, 0, new byte[8]), hash, seconds is { } s ? T0.AddSeconds(s) : null, $"IMG_{n:0000}.jpg");

    private static List<List<int>> Ids(IReadOnlyList<SimilarityGroup> groups) =>
        groups.Select(g => g.Members.Select(m => BitConverter.ToInt32(m.Id.ToByteArray(), 0)).ToList()).ToList();

    [Fact]
    public void CloseHashes_InsideTheWindow_FormOneGroup()
    {
        var groups = SimilarityGrouper.Group(
            [Item(1, 0b0000, 0), Item(2, 0b0001, 1), Item(3, 0b0011, 2), Item(4, ulong.MaxValue, 3)],
            maxDistance: 2,
            timeWindow: TimeSpan.FromSeconds(10));

        Assert.Equal([[1, 2, 3]], Ids(groups));
    }

    [Fact]
    public void TimeWindow_SeparatesSimilarPhotosTakenApart()
    {
        SimilarityItem[] items = [Item(1, 0, 0), Item(2, 0, 5), Item(3, 0, 600), Item(4, 0, 603)];

        Assert.Equal([[1, 2], [3, 4]], Ids(SimilarityGrouper.Group(items, 0, TimeSpan.FromSeconds(10))));
        Assert.Equal([[1, 2, 3, 4]], Ids(SimilarityGrouper.Group(items, 0, timeWindow: null)));
    }

    [Fact]
    public void WithATimeWindow_UndatedPhotosAreNotGrouped_WithoutOneTheyAre()
    {
        SimilarityItem[] items = [Item(1, 0, null), Item(2, 0, null)];

        Assert.Empty(SimilarityGrouper.Group(items, 0, TimeSpan.FromSeconds(10)));
        Assert.Single(SimilarityGrouper.Group(items, 0, null));
    }

    [Fact]
    public void Linking_IsTransitive_AndEachMemberReportsItsNearest()
    {
        // 1–2 differ by 2 bits, 2–3 by 2 bits, 1–3 by 4 bits (> threshold 2).
        var group = Assert.Single(SimilarityGrouper.Group([Item(1, 0b0000, 0), Item(2, 0b0011, 1), Item(3, 0b1111, 2)], 2, null));

        Assert.Equal(3, group.Members.Count);
        var third = group.Members[2];
        Assert.Equal(new Guid(2, 0, 0, new byte[8]), third.NearestId);
        Assert.Equal(2, third.NearestDistance);
        Assert.Equal(1, third.SecondsFromNearest);
    }

    [Fact]
    public void Threshold_IsInclusive()
    {
        SimilarityItem[] items = [Item(1, 0b000, 0), Item(2, 0b111, 1)];
        Assert.Single(SimilarityGrouper.Group(items, 3, null));
        Assert.Empty(SimilarityGrouper.Group(items, 2, null));
    }

    [Fact]
    public void Grouping_IsIndependentOfInputOrder()
    {
        var random = new Random(11);
        var items = Enumerable.Range(1, 80)
            .Select(i => Item(i, (ulong)random.NextInt64() & 0xFF, i % 3 == 0 ? null : random.Next(0, 120)))
            .ToList();
        var expected = SimilarityGrouper.Group(items, 3, TimeSpan.FromSeconds(30));

        for (var round = 0; round < 5; round++)
        {
            var shuffled = items.OrderBy(_ => random.Next()).ToList();
            var actual = SimilarityGrouper.Group(shuffled, 3, TimeSpan.FromSeconds(30));
            Assert.Equal(expected.Select(g => g.Key), actual.Select(g => g.Key));
            Assert.Equal(Ids(expected), Ids(actual));
        }
    }

    [Fact]
    public void ExactGroups_MatchIdenticalContentHashes_CaseInsensitively()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var groups = SimilarityGrouper.GroupExact([(b, "ABC", "b.jpg"), (a, "abc", "a.jpg"), (c, "def", "c.jpg")]);

        Assert.Equal([a, b], Assert.Single(groups));
    }

    [Fact]
    public void SettingsHashes_ChangeOnlyWithRelevantSettings()
    {
        var baseline = AnalysisSettings.Default;
        var finerGrid = baseline with { SharpnessGrid = 6 };
        var biggerImage = baseline with { MaxPixelSize = 2048 };

        Assert.Equal(baseline.ExposureSettingsHash, finerGrid.ExposureSettingsHash);        // grid does not affect exposure
        Assert.NotEqual(baseline.SharpnessSettingsHash, finerGrid.SharpnessSettingsHash);
        Assert.NotEqual(baseline.SharpnessSettingsHash, biggerImage.SharpnessSettingsHash);
        Assert.NotEqual(baseline.PerceptualHashSettingsHash, biggerImage.PerceptualHashSettingsHash);
        Assert.Equal(AnalysisSettings.ContentHashSettingsHash, AnalysisSettings.ContentHashSettingsHash);
    }
}
