namespace FrameForge.Core.Library;

/// <summary>
/// One folder in the folder filter: its full path, a short label and how many photos it covers (its own and its
/// subfolders').
/// </summary>
public sealed record LibraryFolder(string Path, string Label, int Count);

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

        return paths
            .Select(path => new LibraryFolder(
                path,
                Label(path, paths),
                counts.Where(kv => LibraryFilter.IsInFolder(kv.Key, path)).Sum(kv => kv.Value)))
            .ToList();
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
