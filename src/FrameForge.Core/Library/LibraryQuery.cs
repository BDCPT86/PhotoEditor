using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Core.Library;

public enum LibrarySort
{
    /// <summary>Folder, then file name (the order files appear on disk).</summary>
    FolderAndName = 0,

    /// <summary>Capture time, oldest first; photos without a capture time last.</summary>
    CaptureTime = 1,

    /// <summary>File name only, across folders.</summary>
    FileName = 2,

    /// <summary>Highest rating first; unrated last.</summary>
    Rating = 3,
}

/// <summary>Which photos to show. Null sets mean "no restriction".</summary>
public sealed record LibraryFilter
{
    public static LibraryFilter All { get; } = new();

    public IReadOnlySet<CullState>? States { get; init; }

    /// <summary>0 = any rating (including unrated); 1–5 = at least that many stars.</summary>
    public int MinRating { get; init; }

    public IReadOnlySet<ColourLabel>? Labels { get; init; }

    /// <summary>Only missing, unreadable or offline photos.</summary>
    public bool ProblemsOnly { get; init; }

    /// <summary>Folders that can't be reached right now (found at run time, not stored); their photos count as problems.</summary>
    public IReadOnlySet<string>? OfflineFolders { get; init; }

    /// <summary>Only photos in this folder or its subfolders (case-insensitive); null = every folder.</summary>
    public string? Folder { get; init; }

    /// <summary>Only photos first imported at or after this time (UTC); null = any time.</summary>
    public DateTime? ImportedSinceUtc { get; init; }

    /// <summary>True when <paramref name="folderPath"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsInFolder(string folderPath, string folder)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        ArgumentNullException.ThrowIfNull(folder);
        var root = folder.TrimEnd('\\', '/');
        return folderPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || (folderPath.Length > root.Length
                && folderPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && folderPath[root.Length] is '\\' or '/');
    }

    public bool Matches(Photo photo, PhotoDecision decision)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(decision);

        if (Folder is not null && !IsInFolder(photo.FolderPath, Folder))
        {
            return false;
        }

        if (ImportedSinceUtc is { } since && photo.ImportedUtc < since)
        {
            return false;
        }

        if (ProblemsOnly && photo.Status == ImportStatus.Imported && OfflineFolders?.Contains(photo.FolderPath) != true)
        {
            return false;
        }

        if (States is not null && !States.Contains(decision.State))
        {
            return false;
        }

        if (MinRating > 0 && (decision.Rating ?? 0) < MinRating)
        {
            return false;
        }

        return Labels is null || Labels.Contains(decision.Label);
    }
}

/// <summary>
/// Deterministic filtering and sorting. Every sort ends with file path (case-insensitive, ordinal)
/// and then photo id, so equal keys never produce an arbitrary order.
/// </summary>
public static class LibraryQuery
{
    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T> items,
        Func<T, Photo> photoOf,
        Func<T, PhotoDecision> decisionOf,
        LibraryFilter filter,
        LibrarySort sort)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(photoOf);
        ArgumentNullException.ThrowIfNull(decisionOf);
        ArgumentNullException.ThrowIfNull(filter);

        var filtered = items.Where(item => filter.Matches(photoOf(item), decisionOf(item)));

        IOrderedEnumerable<T> ordered = sort switch
        {
            LibrarySort.FolderAndName => filtered
                .OrderBy(i => photoOf(i).FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => photoOf(i).FileName, StringComparer.OrdinalIgnoreCase),
            LibrarySort.CaptureTime => filtered
                .OrderBy(i => photoOf(i).CaptureLocalTime is null ? 1 : 0)
                .ThenBy(i => photoOf(i).CaptureLocalTime),
            LibrarySort.FileName => filtered
                .OrderBy(i => photoOf(i).FileName, StringComparer.OrdinalIgnoreCase),
            LibrarySort.Rating => filtered
                .OrderByDescending(i => decisionOf(i).Rating ?? 0),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown sort."),
        };

        return ordered
            .ThenBy(i => photoOf(i).FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => photoOf(i).Id)
            .ToList();
    }
}
