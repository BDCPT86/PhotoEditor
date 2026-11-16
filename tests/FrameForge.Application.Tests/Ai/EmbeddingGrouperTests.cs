using FrameForge.Application.Ai;

namespace FrameForge.Application.Tests.Ai;

public sealed class EmbeddingGrouperTests
{
    private static readonly DateTime T0 = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified);

    private static float[] Unit(params float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return v.Select(x => x / norm).ToArray();
    }

    private static EmbeddingGrouper.Item Item(string name, float[] vector, int seconds = 0) =>
        new(Guid.NewGuid(), vector, T0.AddSeconds(seconds), name);

    [Fact]
    public void Cosine_OfNormalisedVectors_IsDotProduct()
    {
        Assert.Equal(1, EmbeddingGrouper.Cosine(Unit(1, 2, 3), Unit(1, 2, 3)), 5);
        Assert.Equal(0, EmbeddingGrouper.Cosine(Unit(1, 0), Unit(0, 1)), 5);
        Assert.Equal(0, EmbeddingGrouper.Cosine([1, 0], [1, 0, 0])); // different lengths never match
    }

    [Fact]
    public void GroupsSimilar_AndLeavesDifferentAlone_WithNearestLink()
    {
        var a = Item("a", Unit(1, 0.05f, 0));
        var b = Item("b", Unit(1, 0.10f, 0), 1);
        var c = Item("c", Unit(0, 0, 1), 2);

        var groups = EmbeddingGrouper.Group([c, b, a], 0.9, null);

        var group = Assert.Single(groups);
        Assert.Equal([a.Id, b.Id], group.Members.Select(m => m.Id));
        Assert.Equal(b.Id, group.Members[0].NearestId);
        Assert.Equal(a.Id, group.Members[1].NearestId);
        Assert.True(group.Members[0].Similarity >= 0.9);
    }

    [Fact]
    public void TimeWindow_SplitsSimilarPhotosFarApartInTime()
    {
        var v = Unit(1, 0, 0);
        var groups = EmbeddingGrouper.Group([Item("a", v), Item("b", v, 5), Item("c", v, 600)], 0.9, TimeSpan.FromSeconds(10));

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void ChainsTransitively_AndIsDeterministicRegardlessOfInputOrder()
    {
        var items = new[]
        {
            Item("a", Unit(1, 0.00f)),
            Item("b", Unit(1, 0.35f), 1),
            Item("c", Unit(1, 0.70f), 2),
        };

        var first = EmbeddingGrouper.Group(items, 0.94, null);
        var second = EmbeddingGrouper.Group(items.Reverse().ToArray(), 0.94, null);

        Assert.Equal(3, Assert.Single(first).Members.Count); // a~b and b~c join into one group
        Assert.Equal(first[0].Key, second[0].Key);
        Assert.Equal(first[0].Members.Select(m => m.Id), second[0].Members.Select(m => m.Id));
    }
}
