namespace FrameForge.Application.Ai;

/// <summary>
/// Groups photos whose L2-normalised embeddings have cosine similarity ≥ threshold, optionally within a capture-time
/// window. Union–find, deterministic (sorted by time, then sort key, then id). Each member reports its nearest link.
/// </summary>
public static class EmbeddingGrouper
{
    public sealed record Item(Guid Id, float[] Vector, DateTime? CaptureTime, string SortKey);

    public sealed record Member(Guid Id, Guid NearestId, double Similarity);

    public sealed record SceneGroup(string Key, IReadOnlyList<Member> Members);

    public static IReadOnlyList<SceneGroup> Group(IReadOnlyList<Item> items, double minSimilarity, TimeSpan? timeWindow)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sorted = items
            .OrderBy(i => i.CaptureTime is null ? 1 : 0)
            .ThenBy(i => i.CaptureTime)
            .ThenBy(i => i.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id)
            .ToArray();
        var n = sorted.Length;
        var parent = Enumerable.Range(0, n).ToArray();
        var nearest = Enumerable.Repeat(-1, n).ToArray();
        var best = Enumerable.Repeat(double.MinValue, n).ToArray();

        for (var i = 0; i < n; i++)
        {
            if (timeWindow is not null && sorted[i].CaptureTime is null)
            {
                continue;
            }

            for (var j = i + 1; j < n; j++)
            {
                if (timeWindow is { } w)
                {
                    if (sorted[j].CaptureTime is not { } tj || tj - sorted[i].CaptureTime!.Value > w)
                    {
                        break;
                    }
                }

                var similarity = Cosine(sorted[i].Vector, sorted[j].Vector);
                if (similarity < minSimilarity)
                {
                    continue;
                }

                Union(parent, i, j);
                if (similarity > best[i])
                {
                    best[i] = similarity;
                    nearest[i] = j;
                }

                if (similarity > best[j])
                {
                    best[j] = similarity;
                    nearest[j] = i;
                }
            }
        }

        return Enumerable.Range(0, n)
            .GroupBy(i => Find(parent, i))
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Min())
            .Select(g => new SceneGroup(
                sorted[g.Min()].Id.ToString("N"),
                g.OrderBy(i => i).Select(i => new Member(sorted[i].Id, sorted[nearest[i]].Id, Math.Round(best[i], 4))).ToList()))
            .ToList();
    }

    /// <summary>Dot product of two L2-normalised vectors (cosine similarity).</summary>
    public static double Cosine(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb)
        {
            if (ra < rb)
            {
                parent[rb] = ra;
            }
            else
            {
                parent[ra] = rb;
            }
        }
    }
}
