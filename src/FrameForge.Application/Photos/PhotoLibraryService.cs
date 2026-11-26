using FrameForge.Core.Photos;

namespace FrameForge.Application.Photos;

public sealed class PhotoLibraryService(IPhotoRepository repository) : IPhotoLibraryService
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<int> RemoveAsync(IReadOnlyCollection<Guid> photoIds, CancellationToken cancellationToken = default) =>
        repository.RemoveAsync(photoIds, cancellationToken);
}
