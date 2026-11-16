using FrameForge.Application.Decisions;
using FrameForge.Application.Photos;
using FrameForge.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FrameForge.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFrameForgeApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new PhotoImportOptions());

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPhotoImportService, PhotoImportService>();
        services.AddSingleton<IPhotoLibraryService, PhotoLibraryService>();
        services.AddSingleton<IDecisionService, DecisionService>();
        return services;
    }
}
