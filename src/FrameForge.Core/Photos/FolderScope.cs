namespace FrameForge.Core.Photos;

/// <summary>Case-insensitive folder containment checks for Windows paths.</summary>
public static class FolderScope
{
    /// <summary>True if <paramref name="folder"/> is <paramref name="root"/> or a descendant of it.</summary>
    public static bool IsSameOrUnder(string folder, string root)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(root);

        if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if <paramref name="folder"/> is covered by an import of <paramref name="root"/>.</summary>
    public static bool IsInImportScope(string folder, string root, bool includeSubfolders) =>
        includeSubfolders ? IsSameOrUnder(folder, root) : string.Equals(folder, root, StringComparison.OrdinalIgnoreCase);
}
