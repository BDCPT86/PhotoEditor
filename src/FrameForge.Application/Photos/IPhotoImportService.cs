using FrameForge.Core.Photos;

namespace FrameForge.Application.Photos;

public interface IPhotoImportService
{
    /// <summary>
    /// Imports supported files from a folder. Idempotent: unchanged files are skipped and
    /// records are matched by path. Cancellation stops reading, saves work already done,
    /// and returns a summary with <see cref="ImportSummary.WasCancelled"/> set.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be read.</exception>
    Task<ImportSummary> ImportFolderAsync(
        string folderPath,
        bool includeSubfolders,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IPhotoLibraryService
{
    Task<IReadOnlyList<Photo>> GetPhotosAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes photos from the library with everything stored for them. Their files are not touched.</summary>
    Task<int> RemoveAsync(IReadOnlyCollection<Guid> photoIds, CancellationToken cancellationToken = default);
}
