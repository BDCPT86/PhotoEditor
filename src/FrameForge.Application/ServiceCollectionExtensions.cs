using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Application.Decisions;
using FrameForge.Application.Editing;
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
        services.AddSingleton<IFolderRelocationService, FolderRelocationService>();
        services.AddSingleton<FolderAvailabilityChecker>();
        services.AddSingleton<IDecisionService, DecisionService>();
        services.AddSingleton<Export.IExportProfileService, Export.ExportProfileService>();

        services.TryAddSingleton(new PhotoAnalysisOptions());
        services.AddSingleton<IPhotoAnalysisService, PhotoAnalysisService>();

        services.TryAddSingleton(new AiAnalysisOptions());
        services.AddSingleton<IAiAnalysisService, AiAnalysisService>();

        services.AddSingleton<IEditService, EditService>();
        services.AddSingleton<IEditRenderService, EditRenderService>();

        // Masks for local adjustments (Milestone 08). Requires IAiRuntime and IMaskCache registrations.
        services.AddSingleton<Editing.Masks.IMaskGenerator>(sp => new Editing.Masks.SubjectMaskGenerator(sp.GetRequiredService<IAiRuntime>()));
        services.AddSingleton<Editing.Masks.IMaskGenerator, Editing.Masks.SkyMaskGenerator>();
        services.AddSingleton<Editing.Masks.IMaskService, Editing.Masks.MaskService>();
        return services;
    }
}
