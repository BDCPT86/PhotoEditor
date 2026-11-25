using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Photos;

/// <summary>What was found at a photo's new path.</summary>
public enum RelocationOutcome
{
    /// <summary>Same name, size and modified time: the same file.</summary>
    Found,

    /// <summary>Same name and size, a different modified time (copying can change it). Moved; read again on the next import.</summary>
    ModifiedTimeDiffers,

    /// <summary>A file with that name but a different size: probably not the same photo. Not moved.</summary>
    DifferentSize,

    /// <summary>Nothing at the new path. Not moved.</summary>
    NotFound,

    /// <summary>The new path already belongs to another photo in the library (the moved folder was imported too). Not moved.</summary>
    AlreadyInLibrary,
}

public sealed record RelocationItem(Guid PhotoId, string OldPath, string NewPath, RelocationOutcome Outcome)
{
    public bool WillMove => Outcome is RelocationOutcome.Found or RelocationOutcome.ModifiedTimeDiffers;
}

/// <summary>Where each photo of a folder would go, before anything changes.</summary>
public sealed record RelocationPlan(string OldFolder, string NewFolder, IReadOnlyList<RelocationItem> Items, IReadOnlyList<string> SkippedFolders)
{
    public int Count(RelocationOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    public int Movable => Items.Count(i => i.WillMove);
}

/// <summary>
/// Matches the photos recorded under one folder with the files under the folder's new location, by their path
/// relative to the folder and their size. Pure: the caller scans the new folder.
/// </summary>
public static class FolderRelocationPlanner
{
    public static RelocationPlan Plan(string oldFolder, string newFolder, IReadOnlyList<PhotoFileState> library, PhotoFolderScan newScan)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(newScan);
        var oldRoot = Normalize(oldFolder);
        var newRoot = Normalize(newFolder);
        if (string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The new location is the same folder.", nameof(newFolder));
        }

        var files = newScan.Files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
        var taken = library.ToDictionary(s => s.FilePath, s => s.Id, StringComparer.OrdinalIgnoreCase);
        var items = new List<RelocationItem>();
        foreach (var photo in library.Where(s => FolderScope.IsSameOrUnder(s.FolderPath, oldRoot)).OrderBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var newPath = Path.Combine(newRoot, Path.GetRelativePath(oldRoot, photo.FilePath));
            RelocationOutcome outcome;
            if (taken.TryGetValue(newPath, out var owner) && owner != photo.Id)
            {
                outcome = RelocationOutcome.AlreadyInLibrary;
            }
            else if (!files.TryGetValue(newPath, out var file))
            {
                outcome = RelocationOutcome.NotFound;
            }
            else if (file.FileSizeBytes != photo.FileSizeBytes)
            {
                outcome = RelocationOutcome.DifferentSize;
            }
            else
            {
                outcome = file.LastModifiedUtc == photo.LastModifiedUtc ? RelocationOutcome.Found : RelocationOutcome.ModifiedTimeDiffers;
            }

            items.Add(new RelocationItem(photo.Id, photo.FilePath, newPath, outcome));
        }

        return new RelocationPlan(oldRoot, newRoot, items, newScan.SkippedFolders);
    }

    public static string Normalize(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    }
}

public interface IFolderRelocationService
{
    /// <summary>Scans the new location and says what would happen. Changes nothing.</summary>
    /// <exception cref="DirectoryNotFoundException">The new folder does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The new folder cannot be read.</exception>
    Task<RelocationPlan> PreviewAsync(string oldFolder, string newFolder, CancellationToken cancellationToken);

    /// <summary>Moves the photos the plan matched, all in one transaction. Returns how many moved.</summary>
    Task<int> ApplyAsync(RelocationPlan plan, CancellationToken cancellationToken);
}

public sealed partial class FolderRelocationService(
    IPhotoFileEnumerator enumerator,
    IPhotoRepository repository,
    ILogger<FolderRelocationService> logger) : IFolderRelocationService
{
    public async Task<RelocationPlan> PreviewAsync(string oldFolder, string newFolder, CancellationToken cancellationToken)
    {
        var newRoot = FolderRelocationPlanner.Normalize(newFolder);
        var scan = await Task.Run(() => enumerator.Scan(newRoot, includeSubfolders: true, cancellationToken), cancellationToken).ConfigureAwait(false);
        var library = await repository.GetFileStatesAsync(cancellationToken).ConfigureAwait(false);
        return FolderRelocationPlanner.Plan(oldFolder, newRoot, library, scan);
    }

    public async Task<int> ApplyAsync(RelocationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var moves = plan.Items.Where(i => i.WillMove).Select(i => new PhotoMove(i.PhotoId, i.NewPath)).ToList();
        var moved = await repository.RelocateAsync(moves, cancellationToken).ConfigureAwait(false);
        LogRelocated(plan.OldFolder, plan.NewFolder, moved, plan.Items.Count - moves.Count);
        return moved;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Folder located: {OldFolder} -> {NewFolder}, {Moved} photos moved, {Left} left as they were")]
    private partial void LogRelocated(string oldFolder, string newFolder, int moved, int left);
}
