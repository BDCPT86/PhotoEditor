namespace FrameForge.Application.Export;

/// <summary>
/// Export rules for RAW + JPEG pairs (M09): which files go, how they are counted, and how they are named. A "frame" is
/// one photo as the grid shows it: a pair, or a photo on its own.
/// </summary>
public static class ExportFrames
{
    /// <summary>The files to copy: each pair's files as <paramref name="choice"/> says, every other photo as it is.</summary>
    public static IReadOnlyList<ExportItem> Select(IReadOnlyList<ExportItem> items, PairFiles choice)
    {
        ArgumentNullException.ThrowIfNull(items);
        return choice switch
        {
            PairFiles.RawOnly => items.Where(i => i.Cover is null || i.IsPairRaw).ToList(),
            PairFiles.JpegOnly => items.Where(i => !i.IsPairRaw).ToList(),
            _ => items,
        };
    }

    /// <summary>One item per frame, for edited JPEGs: a pair is rendered once, from its cover.</summary>
    public static IReadOnlyList<ExportItem> Covers(IReadOnlyList<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Where(i => !i.IsPairRaw).ToList();
    }

    public static int Count(IEnumerable<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items as IReadOnlyCollection<ExportItem> ?? items.ToList();
        return list.Count(i => i.Cover is null) + list.Where(i => i.Cover is not null).Select(i => i.Cover!.Id).Distinct().Count();
    }

    public static bool HasPairs(IEnumerable<ExportItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Any(i => i.Cover is not null);
    }

    /// <summary>
    /// The name each item would get under <paramref name="naming"/>, before clashes are resolved. Items of one frame
    /// share a <c>{seq}</c> number. With a pattern, both files of a pair are named from the cover, with their own
    /// extensions; with no pattern each keeps its own name.
    /// </summary>
    /// <param name="extension">An extension for every item (".jpg" for edited JPEGs), or null for each file's own.</param>
    public static IReadOnlyList<(string Name, Guid Frame)> WantedNames(IReadOnlyList<ExportItem> items, ExportNameTemplate naming, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(naming);
        var names = new List<(string, Guid)>(items.Count);
        var sequenceOf = new Dictionary<Guid, int>();
        foreach (var item in items)
        {
            // A photo on its own is always a frame of its own, whatever its id.
            var frame = item.Cover?.Id ?? Guid.NewGuid();
            if (!sequenceOf.TryGetValue(frame, out var sequence))
            {
                sequence = sequenceOf.Count + 1;
                sequenceOf[frame] = sequence;
            }

            var namedFrom = naming.IsOriginal ? item.Photo : item.Cover ?? item.Photo;
            var ext = extension ?? (namedFrom == item.Photo ? null : Path.GetExtension(item.Photo.FileName));
            names.Add((naming.FileNameFor(new ExportNameContext(namedFrom, item.Decision, sequence), ext), frame));
        }

        return names;
    }
}
