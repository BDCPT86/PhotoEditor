using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Infrastructure.Photos;

/// <summary>
/// Walks a folder tree explicitly (rather than with <c>IgnoreInaccessible</c>) so that unreadable
/// sub-folders are reported instead of being skipped silently. Junctions/symlinked folders and
/// system folders (e.g. <c>$RECYCLE.BIN</c>) are not followed, which also prevents cycles.
/// </summary>
public sealed class FileSystemPhotoEnumerator : IPhotoFileEnumerator
{
    private static readonly EnumerationOptions TopLevelOnly = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    public PhotoFolderScan Scan(string folderPath, bool includeSubfolders, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var root = new DirectoryInfo(Path.GetFullPath(folderPath));
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Folder not found: {root.FullName}");
        }

        var files = new List<PhotoFileInfo>();
        var skipped = new List<string>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var isRoot = ReferenceEquals(directory, root);

            try
            {
                foreach (var file in directory.EnumerateFiles("*", TopLevelOnly))
                {
                    if (SupportedPhotoFormats.IsSupported(file.Name))
                    {
                        files.Add(new PhotoFileInfo(file.FullName, file.Length, file.LastWriteTimeUtc));
                    }
                }

                if (includeSubfolders)
                {
                    foreach (var child in directory.EnumerateDirectories("*", TopLevelOnly))
                    {
                        if ((child.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                }
            }
            catch (Exception ex) when (!isRoot && ex is UnauthorizedAccessException or IOException)
            {
                // The root must be readable (the caller reports that); sub-folders are skipped and listed.
                skipped.Add(directory.FullName);
            }
        }

        files.Sort((a, b) => string.Compare(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase));
        return new PhotoFolderScan(files, skipped);
    }
}
