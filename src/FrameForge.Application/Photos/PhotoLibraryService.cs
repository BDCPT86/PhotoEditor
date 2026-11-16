using FrameForge.Core.Photos;

namespace FrameForge.Application.Photos;

public sealed class PhotoLibraryService(IPhotoRepository repository) : IPhotoLibraryService
{
    public Task<IReadOnlyList<Photo>> GetPhotosAsync(CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);
}
