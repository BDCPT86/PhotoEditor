using FrameForge.Core.Photos;

namespace FrameForge.Application.Photos;

/// <summary>A supported file found on disk.</summary>
public sealed record PhotoFileInfo(string FilePath, long FileSizeBytes, DateTime LastModifiedUtc);

/// <summary>Result of scanning a folder for supported files.</summary>
/// <param name="SkippedFolders">Sub-folders that could not be read; scanning continued without them.</param>
public sealed record PhotoFolderScan(IReadOnlyList<PhotoFileInfo> Files, IReadOnlyList<string> SkippedFolders)
{
    /// <summary>
    /// Photos in formats FrameForge doesn't import (<see cref="SupportedPhotoFormats.IsKnownUnsupported"/>), counted by
    /// extension in upper case (".NEF" → 3).
    /// </summary>
    public IReadOnlyDictionary<string, int> UnsupportedFiles { get; init; } = new Dictionary<string, int>();
}

/// <summary>Finds supported photo files on the file system.</summary>
public interface IPhotoFileEnumerator
{
    /// <exception cref="DirectoryNotFoundException">The root folder does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The root folder cannot be read.</exception>
    PhotoFolderScan Scan(string folderPath, bool includeSubfolders, CancellationToken cancellationToken);
}

/// <summary>Reads basic metadata from an image file without decoding its pixels.</summary>
public interface IPhotoMetadataReader
{
    /// <exception cref="PhotoReadException">The file is not a readable image.</exception>
    /// <exception cref="IOException">The file could not be opened.</exception>
    PhotoMetadata Read(string filePath);
}

/// <summary>A photo's new location after its folder moved.</summary>
public sealed record PhotoMove(Guid PhotoId, string NewFilePath);

/// <summary>Minimal stored state used to decide whether a file needs re-reading.</summary>
public sealed record PhotoFileState(Guid Id, string FilePath, string FolderPath, long FileSizeBytes, DateTime LastModifiedUtc, ImportStatus Status);

public interface IPhotoRepository
{
    Task<IReadOnlyList<PhotoFileState>> GetFileStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts new photos and updates existing ones, matched by <see cref="Photo.FilePath"/> (case-insensitive).</summary>
    Task UpsertAsync(IReadOnlyCollection<Photo> photos, CancellationToken cancellationToken = default);

    Task MarkMissingAsync(IReadOnlyCollection<Guid> photoIds, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Points photos at new paths in one transaction (all or none), keeping their identity and everything stored
    /// against it. A photo that was <see cref="ImportStatus.Missing"/> becomes <see cref="ImportStatus.Imported"/>.
    /// </summary>
    /// <returns>The number of photos moved.</returns>
    Task<int> RelocateAsync(IReadOnlyCollection<PhotoMove> moves, CancellationToken cancellationToken = default);

    /// <summary>All photos ordered by folder, then file name.</summary>
    Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates a small JPEG preview of an image.</summary>
public interface IThumbnailGenerator
{
    /// <summary>Changes whenever output would differ (algorithm, quality); part of the cache key.</summary>
    string Version { get; }

    /// <summary>Writes a JPEG whose longest side is at most <paramref name="maxPixelSize"/>, with EXIF orientation applied.</summary>
    /// <exception cref="PhotoReadException">The source is not a readable image.</exception>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    void Generate(string sourcePath, Stream destination, int maxPixelSize, CancellationToken cancellationToken);
}

/// <summary>Disk cache of thumbnails keyed by source identity.</summary>
public interface IThumbnailService
{
    /// <summary>Returns the path of a cached thumbnail, generating it if absent.</summary>
    Task<string> GetOrCreateAsync(Photo photo, CancellationToken cancellationToken = default);
}
