using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Analysis;
using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Analysis;

public sealed class AnalysisPersistenceTests : IDisposable
{
    private readonly TempDirectory _data = new();
    private readonly TempDirectory _photos = new();
    private readonly AppDataPaths _paths;

    public AnalysisPersistenceTests()
    {
        _paths = new AppDataPaths(_data.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _data.Dispose();
        _photos.Dispose();
    }

    private async Task<Photo> AddPhotoAsync(IServiceProvider services, string name, string content = "JPEG")
    {
        var path = Path.Combine(_photos.Path, name);
        File.WriteAllText(path, content);
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            FilePath = path,
            FileName = name,
            FolderPath = _photos.Path,
            FileSizeBytes = new FileInfo(path).Length,
            LastModifiedUtc = File.GetLastWriteTimeUtc(path),
            Status = ImportStatus.Imported,
        };
        await services.GetRequiredService<IPhotoRepository>().UpsertAsync([photo], Ct);
        return photo;
    }

    [Fact]
    public async Task Results_SurviveRestart_AndAreReusedFromTheCache()
    {
        Photo photo;
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            photo = await AddPhotoAsync(first, "a.jpg");
            var summary = await first.GetRequiredService<IPhotoAnalysisService>().AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);
            Assert.Equal(1, summary.Analysed);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);
        var service = second.GetRequiredService<IPhotoAnalysisService>();
        var stored = (await second.GetRequiredService<IPhotoRepository>().GetAllAsync(Ct)).Single();

        var loaded = await service.LoadAsync([stored], AnalysisSettings.Default, Ct);
        var again = await service.AnalyzeAsync([stored], AnalysisSettings.Default, null, Ct);

        Assert.True(loaded[photo.Id].IsComplete);
        Assert.Equal(1, again.Cached);
        Assert.Equal(0, again.Analysed);
    }

    [Fact]
    public async Task Save_ReplacesTheRowForTheSameKind()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var photo = await AddPhotoAsync(services, "a.jpg");
        var repository = services.GetRequiredService<IAnalysisResultRepository>();
        var row = AnalysisCache.Create(photo, AnalysisKind.ContentHash, AnalysisSettings.Default, "first", DateTime.UtcNow);

        await repository.SaveAsync([row], Ct);
        await repository.SaveAsync([row with { ValueJson = "\"second\"" }], Ct);

        var stored = Assert.Single(await repository.GetAllAsync(Ct));
        Assert.Equal("\"second\"", stored.ValueJson);
        Assert.Equal(photo.LastModifiedUtc, stored.SourceModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, stored.SourceModifiedUtc.Kind);
    }

    [Fact]
    public async Task DeletingAPhoto_DeletesItsResults()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var photo = await AddPhotoAsync(services, "a.jpg");
        await services.GetRequiredService<IPhotoAnalysisService>().AnalyzeAsync([photo], AnalysisSettings.Default, null, Ct);

        await using (var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Photos.Where(p => p.Id == photo.Id).ExecuteDeleteAsync(Ct);
        }

        Assert.Empty(await services.GetRequiredService<IAnalysisResultRepository>().GetAllAsync(Ct));
    }

    [Fact]
    public async Task IdenticalFiles_GetTheSameContentHash()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var a = await AddPhotoAsync(services, "a.jpg", "same bytes");
        var b = await AddPhotoAsync(services, "b.jpg", "same bytes");
        var c = await AddPhotoAsync(services, "c.jpg", "other bytes");
        var service = services.GetRequiredService<IPhotoAnalysisService>();

        await service.AnalyzeAsync([a, b, c], AnalysisSettings.Default, null, Ct);
        var loaded = await service.LoadAsync([a, b, c], AnalysisSettings.Default, Ct);

        Assert.Equal(loaded[a.Id].ContentHash, loaded[b.Id].ContentHash);
        Assert.NotEqual(loaded[a.Id].ContentHash, loaded[c.Id].ContentHash);
    }

    [Fact]
    public void Sha256_MatchesKnownValue()
    {
        var path = Path.Combine(_photos.Path, "abc.bin");
        File.WriteAllText(path, "abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", new Sha256FileHasher().ComputeSha256(path, Ct));
    }

    [Fact]
    public void Sha256_HonoursCancellation_AndDoesNotModifyTheFile()
    {
        var path = Path.Combine(_photos.Path, "big.bin");
        File.WriteAllBytes(path, new byte[3 * 1024 * 1024]);
        var modified = File.GetLastWriteTimeUtc(path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new Sha256FileHasher().ComputeSha256(path, cts.Token));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
    }
}
