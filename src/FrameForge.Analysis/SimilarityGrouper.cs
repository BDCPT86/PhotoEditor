namespace FrameForge.Analysis;

/// <param name="SortKey">Tie-breaker for deterministic order (e.g. file path).</param>
public sealed record SimilarityItem(Guid Id, ulong Hash, DateTime? CaptureTime, string SortKey);

/// <param name="NearestId">The group member this photo is most similar to.</param>
/// <param name="NearestDistance">Bits that differ from <paramref name="NearestId"/> (0–64).</param>
/// <param name="SecondsFromNearest">Capture-time gap to the nearest member, if both have one.</param>
public sealed record SimilarityMember(Guid Id, Guid NearestId, int NearestDistance, double? SecondsFromNearest);

/// <param name="Key">Stable identifier: the first member's id in sorted order.</param>
public sealed record SimilarityGroup(string Key, IReadOnlyList<SimilarityMember> Members);

/// <summary>
/// Groups photos whose perceptual hashes differ in at most <c>maxDistance</c> bits, optionally only if captured
/// within a time window (bursts). Linking is transitive (union–find): A~B and B~C puts A, B, C in one group even if
/// A and C are further apart; each member reports its own nearest link so this is visible. Deterministic: inputs are
/// sorted by capture time and sort key before linking.
/// </summary>
public static class SimilarityGrouper
{
    public const string Version = "phash-hamming-unionfind-v1";

    public static IReadOnlyList<SimilarityGroup> Group(IReadOnlyList<SimilarityItem> items, int maxDistance, TimeSpan? timeWindow)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);

        var sorted = items
            .OrderBy(i => i.CaptureTime is null ? 1 : 0)
            .ThenBy(i => i.CaptureTime)
            .ThenBy(i => i.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id)
            .ToArray();
        var n = sorted.Length;
        var parent = Enumerable.Range(0, n).ToArray();
        var nearest = new int[n];
        var nearestDistance = new int[n];
        Array.Fill(nearest, -1);
        Array.Fill(nearestDistance, int.MaxValue);

        for (var i = 0; i < n; i++)
        {
            if (timeWindow is { } window && sorted[i].CaptureTime is null)
            {
                continue; // without a capture time a photo cannot be placed in a burst
            }

            for (var j = i + 1; j < n; j++)
            {
                if (timeWindow is { } w)
                {
                    if (sorted[j].CaptureTime is not { } tj)
                    {
                        break; // undated items sort last
                    }

                    if (tj - sorted[i].CaptureTime!.Value > w)
                    {
                        break; // sorted by time: nothing later can be inside the window
                    }
                }

                var distance = PerceptualHasher.Distance(sorted[i].Hash, sorted[j].Hash);
                if (distance > maxDistance)
                {
                    continue;
                }

                Union(parent, i, j);
                Track(nearest, nearestDistance, i, j, distance);
                Track(nearest, nearestDistance, j, i, distance);
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(parent, i);
            if (!groups.TryGetValue(root, out var list))
            {
                groups[root] = list = [];
            }

            list.Add(i);
        }

        return groups.Values
            .Where(g => g.Count > 1)
            .OrderBy(g => g[0])
            .Select(g => new SimilarityGroup(
                sorted[g[0]].Id.ToString("N"),
                g.Select(i => new SimilarityMember(
                    sorted[i].Id,
                    sorted[nearest[i]].Id,
                    nearestDistance[i],
                    sorted[i].CaptureTime is { } a && sorted[nearest[i]].CaptureTime is { } b ? Math.Abs((a - b).TotalSeconds) : null)).ToList()))
            .ToList();
    }

    /// <summary>Groups by identical content hash. Deterministic order by sort key.</summary>
    public static IReadOnlyList<IReadOnlyList<Guid>> GroupExact(IEnumerable<(Guid Id, string ContentHash, string SortKey)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .GroupBy(i => i.ContentHash, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => (IReadOnlyList<Guid>)g.OrderBy(i => i.SortKey, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).Select(i => i.Id).ToList())
            .OrderBy(g => g[0])
            .ToList();
    }

    private static void Track(int[] nearest, int[] nearestDistance, int from, int to, int distance)
    {
        if (distance < nearestDistance[from])
        {
            nearestDistance[from] = distance;
            nearest[from] = to;
        }
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
            // Lower index becomes root: keeps the root the earliest member (deterministic).
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
