using FrameForge.Application;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.TestSupport;

internal static class TestServices
{
    /// <summary>Production registrations against a temp data folder, with fakes for imaging.</summary>
    public static ServiceProvider Build(AppDataPaths paths, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(new PhotoImportOptions { MaxParallelism = 2, BatchSize = 25, ProgressInterval = TimeSpan.Zero });
        configure?.Invoke(services);

        services
            .AddFrameForgeApplication()
            .AddFrameForgeInfrastructure(paths);

        services.AddSingleton<IPhotoMetadataReader, StubMetadataReader>();
        services.AddSingleton<IThumbnailGenerator, StubThumbnailGenerator>();
        services.AddSingleton<Application.Analysis.IAnalysisImageLoader, StubAnalysisImageLoader>();
        services.AddSingleton<Application.Ai.IAiRuntime, NoModelsAiRuntime>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static async Task<ServiceProvider> BuildMigratedAsync(AppDataPaths paths, CancellationToken cancellationToken, Action<IServiceCollection>? configure = null)
    {
        var provider = Build(paths, configure);
        await provider.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken);
        return provider;
    }
}

/// <summary>Treats files whose content starts with "BAD" as corrupt; everything else gets fixed metadata.</summary>
internal sealed class StubMetadataReader : IPhotoMetadataReader
{
    public PhotoMetadata Read(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 'B' && bytes[1] == 'A' && bytes[2] == 'D')
        {
            throw new PhotoReadException("stub: corrupt");
        }

        return new PhotoMetadata { Width = 10, Height = 20, CaptureLocalTime = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Unspecified) };
    }
}

/// <summary>A flat grey image for any existing file (imaging itself is tested in FrameForge.Imaging.Tests).</summary>
internal sealed class StubAnalysisImageLoader : Application.Analysis.IAnalysisImageLoader
{
    public FrameForge.Analysis.AnalysisImage Load(string path, int maxPixelSize)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("missing", path);
        }

        return FrameForge.Analysis.AnalysisImage.Create(16, 16, (_, _) => (128, 128, 128));
    }
}

/// <summary>AI runtime with no models installed (ONNX inference is tested in FrameForge.AI.Tests).</summary>
internal sealed class NoModelsAiRuntime : Application.Ai.IAiRuntime
{
    public string? LastError => null;

    public Application.Ai.IFaceDetector? GetFaceDetector() => null;

    public Application.Ai.IImageEmbedder? GetImageEmbedder() => null;

    public void Reset()
    {
    }
}

internal sealed class StubThumbnailGenerator : IThumbnailGenerator
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public string Version => "stub-v1";

    public void Generate(string sourcePath, Stream destination, int maxPixelSize, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("missing", sourcePath);
        }

        Interlocked.Increment(ref _calls);
        destination.Write("THUMB"u8);
    }
}
