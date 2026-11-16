using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Application.Tests.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Ai;

public sealed class AiInsightsTests
{
    private static readonly DateTime T0 = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static List<Photo> Photos(int count)
    {
        var photos = Enumerable.Range(0, count).Select(i => AnalysisTestPhotos.Create($"IMG_{i}.jpg")).ToList();
        for (var i = 0; i < count; i++)
        {
            photos[i].CaptureLocalTime = T0.AddSeconds(i);
        }

        return photos;
    }

    private static PhotoAnalysis Sharpness(double score) => new()
    {
        Sharpness = new SharpnessResult(score, score / 2, 4, 0, 0, new double[16], 1024, 683),
    };

    private static FaceFinding Face(double sharpness, float score = 0.9f) => new(0.2f, 0.2f, 0.2f, 0.2f, score, sharpness);

    private static AiPhotoAnalysis WithFaces(params FaceFinding[] faces) => new() { Faces = new FacesResult(faces) };

    [Fact]
    public void Rank_WithFaces_PicksPhotoWhoseSoftestFaceIsSharpest()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var faces = new Dictionary<Guid, IReadOnlyList<FaceFinding>>
        {
            [a] = [Face(200), Face(30)],   // one soft face
            [b] = [Face(120), Face(110)],  // both reasonably sharp
        };

        var ranking = AiInsights.Rank([a, b], id => faces[id], new Dictionary<Guid, PhotoAnalysis>());

        Assert.NotNull(ranking);
        Assert.Equal(b, ranking.Value.Best);
        Assert.Equal(SuggestionConfidence.High, ranking.Value.Confidence); // (110 − 30) / 110 ≈ 73 %
        Assert.Contains("softest main face", ranking.Value.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100, 95, SuggestionConfidence.Low)]
    [InlineData(100, 85, SuggestionConfidence.Medium)]
    [InlineData(100, 50, SuggestionConfidence.High)]
    public void Rank_WithoutFaces_UsesWholePhotoSharpness_ConfidenceFromMargin(double best, double next, SuggestionConfidence expected)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var deterministic = new Dictionary<Guid, PhotoAnalysis> { [a] = Sharpness(next), [b] = Sharpness(best) };

        var ranking = AiInsights.Rank([a, b], _ => [], deterministic);

        Assert.Equal(b, ranking!.Value.Best);
        Assert.Equal(expected, ranking.Value.Confidence);
    }

    [Fact]
    public void Rank_WithOnlyOnePhotoHavingAFace_FallsBackToWholePhotoSharpness()
    {
        // A single (possibly false) face must not stop the group from getting a pick.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var deterministic = new Dictionary<Guid, PhotoAnalysis> { [a] = Sharpness(100), [b] = Sharpness(300) };

        var ranking = AiInsights.Rank([a, b], id => id == a ? [Face(500)] : [], deterministic);

        Assert.Equal(b, ranking!.Value.Best);
        Assert.Contains("highest sharpness", ranking.Value.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFaces_ExcludeSmallBackgroundAndUnmeasuredFaces()
    {
        var subject = new FaceFinding(0.4f, 0.2f, 0.20f, 0.25f, 0.9f, 300);
        var second = new FaceFinding(0.1f, 0.2f, 0.12f, 0.15f, 0.9f, 250);   // ≥ half the subject's width
        var spectator = new FaceFinding(0.8f, 0.1f, 0.03f, 0.04f, 0.9f, 15); // small, blurred background face
        var tiny = new FaceFinding(0.9f, 0.1f, 0.30f, 0.30f, 0.9f, null);    // too small to measure (no sharpness)

        Assert.Equal([subject, second], AiInsights.MainFaces([subject, second, spectator, tiny]));
        Assert.Empty(AiInsights.MainFaces([tiny]));
    }

    [Fact]
    public void BlurredSpectator_DoesNotMakeASharpSubjectSoft()
    {
        var photos = Photos(1);
        var ai = new Dictionary<Guid, AiPhotoAnalysis>
        {
            [photos[0].Id] = WithFaces(new FaceFinding(0.4f, 0.2f, 0.2f, 0.25f, 0.9f, 300), new FaceFinding(0.8f, 0.1f, 0.03f, 0.04f, 0.9f, 5)),
        };

        var insight = AiInsights.Build(photos, ai, new Dictionary<Guid, PhotoAnalysis>(), new Dictionary<Guid, PhotoInsight>(), AiThresholds.Default)[photos[0].Id];

        Assert.Equal(2, insight.FaceCount);
        Assert.False(insight.FacesSoft);
        Assert.Contains("1 main", insight.Details.Single().Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Rank_NeedsTwoScoredMembers()
    {
        var a = Guid.NewGuid();
        Assert.Null(AiInsights.Rank([a, Guid.NewGuid()], _ => [], new Dictionary<Guid, PhotoAnalysis> { [a] = Sharpness(100) }));
    }

    [Fact]
    public void Faces_BelowScoreThreshold_AreNotCounted_AndSoftnessUsesThreshold()
    {
        var photos = Photos(1);
        var ai = new Dictionary<Guid, AiPhotoAnalysis> { [photos[0].Id] = WithFaces(Face(20, 0.95f), Face(500, 0.55f)) };

        var insight = AiInsights.Build(photos, ai, new Dictionary<Guid, PhotoAnalysis>(), new Dictionary<Guid, PhotoInsight>(), AiThresholds.Default)[photos[0].Id];

        Assert.Equal(1, insight.FaceCount);      // the 0.55 detection is below the 0.6 default
        Assert.True(insight.FacesSoft);          // the counted face (20) is below 40
        var line = Assert.Single(insight.Details);
        Assert.Contains(AiInsights.Disclaimer, line.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneGroup_GetsOnePick_WithReasonAndDisclaimer()
    {
        var photos = Photos(3);
        var v = new float[] { 1, 0, 0 };
        var ai = photos.ToDictionary(p => p.Id, _ => new AiPhotoAnalysis { Embedding = v });
        var deterministic = new Dictionary<Guid, PhotoAnalysis>
        {
            [photos[0].Id] = Sharpness(50),
            [photos[1].Id] = Sharpness(300),
            [photos[2].Id] = Sharpness(80),
        };

        var insights = AiInsights.Build(photos, ai, deterministic, new Dictionary<Guid, PhotoInsight>(), AiThresholds.Default);

        Assert.All(photos, p => Assert.Equal(3, insights[p.Id].SceneGroupSize));
        var pick = Assert.Single(insights, kv => kv.Value.IsSuggestedPick);
        Assert.Equal(photos[1].Id, pick.Key);
        Assert.Equal(SuggestionConfidence.High, pick.Value.PickConfidence);
        var line = Assert.Single(pick.Value.Details, l => l.Topic == "AI · Suggested pick");
        Assert.Contains("scene group", line.Explanation, StringComparison.Ordinal);
        Assert.Contains(AiInsights.Disclaimer, line.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAiResults_FallsBackToSimilarPhotoGroups()
    {
        var photos = Photos(2);
        var deterministic = new Dictionary<Guid, PhotoAnalysis> { [photos[0].Id] = Sharpness(200), [photos[1].Id] = Sharpness(100) };
        var groups = photos.ToDictionary(p => p.Id, _ => PhotoInsight.NotAnalysed with { GroupKey = "g1", GroupSize = 2 });

        var insights = AiInsights.Build(photos, new Dictionary<Guid, AiPhotoAnalysis>(), deterministic, groups, AiThresholds.Default);

        var pick = Assert.Single(insights);
        Assert.Equal(photos[0].Id, pick.Key);
        Assert.Contains("similar-photo group", pick.Value.Details.Single().Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAi_NoGroups_NoInsights()
    {
        var photos = Photos(3);
        Assert.Empty(AiInsights.Build(photos, new Dictionary<Guid, AiPhotoAnalysis>(), new Dictionary<Guid, PhotoAnalysis>(), new Dictionary<Guid, PhotoInsight>(), AiThresholds.Default));
    }
}
