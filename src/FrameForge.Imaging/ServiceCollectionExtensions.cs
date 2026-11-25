using FrameForge.Application.Photos;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Imaging;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFrameForgeImaging(this IServiceCollection services)
    {
        services.AddSingleton<FrameForge.Raw.IRawReader, FrameForge.Raw.RawReader>();
        services.AddSingleton<WicPhotoMetadataReader>();
        services.AddSingleton<RawPhotoMetadataReader>();
        services.AddSingleton<IPhotoMetadataReader, PhotoMetadataReader>();
        services.AddSingleton<IThumbnailGenerator, WicThumbnailGenerator>();
        services.AddSingleton<IPhotoBitmapLoader, WicPhotoBitmapLoader>();
        services.AddSingleton<FrameForge.Application.Analysis.IAnalysisImageLoader, WicAnalysisImageLoader>();
        services.AddSingleton<FrameForge.Application.Editing.IJpegEncoder, WicJpegEncoder>();
        return services;
    }
}
