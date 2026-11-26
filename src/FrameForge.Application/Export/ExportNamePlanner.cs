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

    /// <summary>
    /// Like <see cref="Plan"/>, but the names of one frame (the files of a RAW + JPEG pair) get the same " (n)", the
    /// first that is free for all of them, so <c>IMG_1.CR3</c> and <c>IMG_1.JPG</c> stay a matching pair. The result is
    /// in the order given.
    /// </summary>
    public static IReadOnlyList<PlannedFile> PlanFrames(IReadOnlyList<(string Name, Guid Frame)> names, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(existingNames);

        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var chosen = new string[names.Count];
        var indexesOf = new Dictionary<Guid, List<int>>();
        var order = new List<Guid>();
        for (var i = 0; i < names.Count; i++)
        {
            var frame = names[i].Frame;
            if (indexesOf.TryGetValue(frame, out var shared)
                && shared.Any(j => string.Equals(Path.GetFileName(names[j].Name), Path.GetFileName(names[i].Name), StringComparison.OrdinalIgnoreCase)))
            {
                // The same name twice in one frame can never be told apart by a shared number: plan it on its own.
                frame = Guid.NewGuid();
            }

            if (!indexesOf.TryGetValue(frame, out var indexes))
            {
                indexes = [];
                indexesOf[frame] = indexes;
                order.Add(frame);
            }

            indexes.Add(i);
        }

        foreach (var frame in order)
        {
            var indexes = indexesOf[frame];
            for (var n = 1; ; n++)
            {
                var candidates = indexes.Select(i => WithNumber(Path.GetFileName(names[i].Name), n)).ToList();
                if (candidates.All(c => !taken.Contains(c)) && candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count() == candidates.Count)
                {
                    for (var k = 0; k < indexes.Count; k++)
                    {
                        chosen[indexes[k]] = candidates[k];
                        taken.Add(candidates[k]);
                    }

                    break;
                }
            }
        }

        return names
            .Select((name, i) => new PlannedFile(name.Name, chosen[i], !string.Equals(chosen[i], Path.GetFileName(name.Name), StringComparison.Ordinal)))
            .ToList();
    }

    private static string WithNumber(string fileName, int n) => n == 1
        ? fileName
        : string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}");

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
