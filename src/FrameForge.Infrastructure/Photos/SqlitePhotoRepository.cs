using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrameForge.Infrastructure.Photos;

public sealed class SqlitePhotoRepository(IDbContextFactory<FrameForgeDbContext> contextFactory) : IPhotoRepository
{
    // Microsoft.Data.Sqlite performs I/O synchronously even behind async APIs,
    // so each operation runs on the thread pool to keep callers responsive.

    public Task<IReadOnlyList<PhotoFileState>> GetFileStatesAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<PhotoFileState>>(
            async context => await context.Photos
                .AsNoTracking()
                .Select(p => new PhotoFileState(p.Id, p.FilePath, p.FolderPath, p.FileSizeBytes, p.LastModifiedUtc, p.Status))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<Photo>>(
            async context => await context.Photos
                .AsNoTracking()
                .OrderBy(p => p.FolderPath)
                .ThenBy(p => p.FileName)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);

    public Task UpsertAsync(IReadOnlyCollection<Photo> photos, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);
        if (photos.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunAsync(
            async context =>
            {
                var paths = photos.Select(p => p.FilePath).ToList();

                // FilePath uses NOCASE collation, so this lookup is case-insensitive in SQL.
                var existing = await context.Photos
                    .Where(p => paths.Contains(p.FilePath))
                    .ToDictionaryAsync(p => p.FilePath, StringComparer.OrdinalIgnoreCase, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var photo in photos)
                {
                    if (existing.TryGetValue(photo.FilePath, out var stored))
                    {
                        CopyImportedFields(photo, stored);
                    }
                    else
                    {
                        context.Photos.Add(Clone(photo));
                    }
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task MarkMissingAsync(IReadOnlyCollection<Guid> photoIds, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photoIds);
        if (photoIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var ids = photoIds.ToList();
        return RunAsync(
            async context => await context.Photos
                .Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(p => p.Status, ImportStatus.Missing)
                        .SetProperty(p => p.LastError, reason),
                    cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);
    }

    public Task<int> RelocateAsync(IReadOnlyCollection<PhotoMove> moves, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (moves.Count == 0)
        {
            return Task.FromResult(0);
        }

        var byId = moves.ToDictionary(m => m.PhotoId);
        return RunAsync(
            async context =>
            {
                var ids = byId.Keys.ToList();
                var photos = await context.Photos.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var photo in photos)
                {
                    var path = byId[photo.Id].NewFilePath;
                    photo.FilePath = path;
                    photo.FileName = Path.GetFileName(path);
                    photo.FolderPath = Path.GetDirectoryName(path) ?? path;
                    if (photo.Status == ImportStatus.Missing)
                    {
                        photo.Status = ImportStatus.Imported;
                        photo.LastError = null;
                    }
                }

                // One SaveChanges is one transaction: a clash on the unique path index rolls every move back.
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return photos.Count;
            },
            cancellationToken);
    }

    /// <summary>Updates everything an import can change; keeps identity and first-import time.</summary>
    private static void CopyImportedFields(Photo source, Photo target)
    {
        target.FilePath = source.FilePath;
        target.FileName = source.FileName;
        target.FolderPath = source.FolderPath;
        target.FileSizeBytes = source.FileSizeBytes;
        target.LastModifiedUtc = source.LastModifiedUtc;
        target.Width = source.Width;
        target.Height = source.Height;
        target.CaptureLocalTime = source.CaptureLocalTime;
        target.CameraMake = source.CameraMake;
        target.CameraModel = source.CameraModel;
        target.LensModel = source.LensModel;
        target.Iso = source.Iso;
        target.ExposureTime = source.ExposureTime;
        target.FNumber = source.FNumber;
        target.FocalLengthMm = source.FocalLengthMm;
        target.Orientation = source.Orientation;
        target.Status = source.Status;
        target.LastError = source.LastError;
    }

    private static Photo Clone(Photo source)
    {
        var copy = new Photo
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            FilePath = source.FilePath,
            FileName = source.FileName,
            FolderPath = source.FolderPath,
            ImportedUtc = source.ImportedUtc,
        };
        CopyImportedFields(source, copy);
        return copy;
    }

    private Task<T> RunAsync<T>(Func<FrameForgeDbContext, Task<T>> operation, CancellationToken cancellationToken) =>
        Task.Run(
            async () =>
            {
                var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using (context.ConfigureAwait(false))
                {
                    return await operation(context).ConfigureAwait(false);
                }
            },
            cancellationToken);
}
