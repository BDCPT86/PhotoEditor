namespace FrameForge.Core.Library;

/// <summary>
/// One folder in the folder filter: its full path, a short label and how many photos it covers (its own and its
/// subfolders').
/// </summary>
public sealed record LibraryFolder(string Path, string Label, int Count)
{
    /// <summary>Nesting level under the nearest listed ancestor folder; 0 for a top-level folder.</summary>
    public int Depth { get; init; }

    /// <summary>
    /// Name for a tree: the path below the nearest listed ancestor ("Day 2", or "2026\Day 2" when the folder in
    /// between holds no photos); the <see cref="Label"/> for a top-level folder.
    /// </summary>
    public string Name { get; init; } = Label;
}

/// <summary>Builds the folder filter's list from the folders of the photos in the library.</summary>
public static class LibraryFolders
{
    /// <summary>
    /// Every folder that holds photos, sorted by path. The label is the folder's name; when two folders share a name,
    /// as much of the path as tells them apart is shown ("2026\Randoms" and "2025\Randoms").
    /// </summary>
    public static IReadOnlyList<LibraryFolder> Build(IEnumerable<string> photoFolders)
    {
        ArgumentNullException.ThrowIfNull(photoFolders);
        var counts = photoFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .GroupBy(f => f.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.First().TrimEnd('\\', '/'), g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var paths = counts.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LibraryFolder>(paths.Count);
        foreach (var path in paths)
        {
            // Sorted by path, so every ancestor is handled before its descendants.
            var parent = paths
                .Where(other => other.Length < path.Length && LibraryFilter.IsInFolder(path, other))
                .MaxBy(other => other.Length);
            var depth = parent is null ? 0 : depths[parent] + 1;
            depths[path] = depth;
            var label = Label(path, paths);
            result.Add(new LibraryFolder(path, label, counts.Where(kv => LibraryFilter.IsInFolder(kv.Key, path)).Sum(kv => kv.Value))
            {
                Depth = depth,
                Name = parent is null ? label : path[(parent.Length + 1)..],
            });
        }

        return result;
    }

    /// <summary>The shortest tail of <paramref name="path"/> (in folder names) that no other folder shares.</summary>
    internal static string Label(string path, IReadOnlyList<string> all)
    {
        var parts = Split(path);
        for (var take = 1; take <= parts.Length; take++)
        {
            var tail = string.Join('\\', parts[^take..]);
            if (all.Count(other => string.Equals(Tail(other, take), tail, StringComparison.OrdinalIgnoreCase)) == 1)
            {
                return tail;
            }
        }

        return path;
    }

    private static string Tail(string path, int take)
    {
        var parts = Split(path);
        return string.Join('\\', parts[^Math.Min(take, parts.Length)..]);
    }

    private static string[] Split(string path) => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
}
