using FrameForge.Application.Photos;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Imaging;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFrameForgeImaging(this IServiceCollection services)
    {
        services.AddSingleton<IPhotoMetadataReader, WicPhotoMetadataReader>();
        services.AddSingleton<IThumbnailGenerator, WicThumbnailGenerator>();
        services.AddSingleton<IPhotoBitmapLoader, WicPhotoBitmapLoader>();
        return services;
    }
}
