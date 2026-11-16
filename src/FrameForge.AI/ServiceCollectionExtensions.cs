using FrameForge.Application.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.AI;

public static class ServiceCollectionExtensions
{
    /// <remarks>Requires an <see cref="IModelStore"/> registration (FrameForge.Infrastructure).</remarks>
    public static IServiceCollection AddFrameForgeAi(this IServiceCollection services)
    {
        services.AddSingleton<OnnxAiRuntime>();
        services.AddSingleton<IAiRuntime>(sp => sp.GetRequiredService<OnnxAiRuntime>());
        return services;
    }
}
