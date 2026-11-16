using FrameForge.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFrameForgeApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        return services;
    }
}
