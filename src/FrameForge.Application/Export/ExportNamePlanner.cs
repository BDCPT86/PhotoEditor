using System.Globalization;

namespace FrameForge.Application.Export;

/// <summary>
/// Chooses destination file names that never collide, case-insensitively, with names already in the
/// destination or with each other ("IMG_0001.jpg", "IMG_0001 (2).jpg", …). Pure and deterministic:
/// sources are assigned in the order given.
/// </summary>
public static class ExportNamePlanner
{
    public sealed record PlannedFile(string SourcePath, string TargetFileName, bool WasRenamed);

    public static IReadOnlyList<PlannedFile> Plan(IEnumerable<string> sourcePaths, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(existingNames);

        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var plan = new List<PlannedFile>();
        foreach (var source in sourcePaths)
        {
            var original = Path.GetFileName(source);
            var name = NextAvailable(original, taken);
            taken.Add(name);
            plan.Add(new PlannedFile(source, name, !string.Equals(name, original, StringComparison.Ordinal)));
        }

        return plan;
    }

    /// <summary>The first of "name.ext", "name (2).ext", "name (3).ext"… not in <paramref name="taken"/>.</summary>
    public static string NextAvailable(string fileName, IReadOnlySet<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(taken);

        if (!taken.Contains(fileName))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
