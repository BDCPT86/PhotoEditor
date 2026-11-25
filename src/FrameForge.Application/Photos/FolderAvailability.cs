namespace FrameForge.Application.Photos;

/// <summary>Whether a folder on disk can be reached.</summary>
public interface IFolderProbe
{
    /// <summary>True if the folder exists and can be seen. May block on a slow network drive.</summary>
    bool Exists(string folderPath);
}

public enum FolderOffline
{
    /// <summary>The drive or network share is not there (unplugged, not mapped, or not responding).</summary>
    DriveUnavailable,

    /// <summary>The drive is there but the folder is not (moved, renamed or deleted).</summary>
    FolderNotFound,
}

/// <summary>A library folder that cannot be reached right now, and why in plain words.</summary>
public sealed record OfflineFolder(string Folder, FolderOffline Kind, string Reason);

/// <summary>
/// Finds library folders that can't be reached. Checks each drive (or share) once, with a time limit so an
/// unreachable network drive can't stall the library, then each folder on drives that are there. Nothing is stored:
/// a folder is offline only while it is unreachable.
/// </summary>
public sealed class FolderAvailabilityChecker(IFolderProbe probe)
{
    public static readonly TimeSpan DriveTimeout = TimeSpan.FromSeconds(3);

    /// <returns>Only the folders that are offline, keyed by folder path (case-insensitive).</returns>
    public async Task<IReadOnlyDictionary<string, OfflineFolder>> CheckAsync(IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var result = new Dictionary<string, OfflineFolder>(StringComparer.OrdinalIgnoreCase);
        var byRoot = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(f => Path.GetPathRoot(f) ?? f, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = group.Key;
            if (!await RootIsThereAsync(root, cancellationToken).ConfigureAwait(false))
            {
                var reason = DriveReason(root);
                foreach (var folder in group)
                {
                    result[folder] = new OfflineFolder(folder, FolderOffline.DriveUnavailable, reason);
                }

                continue;
            }

            var missing = await Task.Run(() => group.Where(f => !probe.Exists(f)).ToList(), cancellationToken).ConfigureAwait(false);
            foreach (var folder in missing)
            {
                result[folder] = new OfflineFolder(folder, FolderOffline.FolderNotFound, "This folder isn't there any more. It may have been moved or renamed: use Locate folder… to find it.");
            }
        }

        return result;
    }

    /// <summary>"Drive E: isn't connected." or "\\nas\photos can't be reached."</summary>
    public static string DriveReason(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var trimmed = root.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':'
            ? $"Drive {trimmed.ToUpperInvariant()} isn't connected."
            : $"{trimmed} can't be reached.";
    }

    private async Task<bool> RootIsThereAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => probe.Exists(root), CancellationToken.None).WaitAsync(DriveTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false; // not responding counts as unavailable; the probe keeps running on its own thread
        }
    }
}
