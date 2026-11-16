using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Infrastructure.Photos;

/// <summary>
/// Thumbnail cache on disk under <see cref="AppDataPaths.ThumbnailsDirectory"/>. Files are named by
/// <see cref="ThumbnailKey"/>, so a missing or stale thumbnail is simply regenerated.
/// </summary>
public sealed class DiskThumbnailService(IThumbnailGenerator generator, AppDataPaths paths) : IThumbnailService
{
    /// <summary>Longest side in pixels. Large enough for ~200 DIP tiles at 150% display scaling.</summary>
    public const int MaxPixelSize = 320;

    public Task<string> GetOrCreateAsync(Photo photo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);

        var key = ThumbnailKey.Compute(photo, MaxPixelSize, generator.Version);
        var path = GetCachePath(key);
        if (File.Exists(path))
        {
            return Task.FromResult(path);
        }

        return Task.Run(() => Generate(photo.FilePath, path, cancellationToken), cancellationToken);
    }

    public string GetCachePath(string key) =>
        Path.Combine(paths.ThumbnailsDirectory, key[..2], key + ".jpg");

    private string Generate(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Write to a temp file and move into place, so a crash or concurrent request
        // never leaves a partially written thumbnail under the final name.
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                generator.Generate(sourcePath, stream, MaxPixelSize, cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            return targetPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
