using FrameForge.Application.Decisions;
using FrameForge.Application.Photos;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Decisions;

public sealed class SqliteDecisionRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;

    public SqliteDecisionRepositoryTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private static async Task<Guid> AddPhotoAsync(IServiceProvider services, string name)
    {
        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            FilePath = $@"C:\Shoot\{name}",
            FileName = name,
            FolderPath = @"C:\Shoot",
            Status = ImportStatus.Imported,
        };
        await services.GetRequiredService<IPhotoRepository>().UpsertAsync([photo], Ct);
        return photo.Id;
    }

    [Fact]
    public async Task Decisions_SurviveRestart()
    {
        Guid keepId, rejectId;
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            keepId = await AddPhotoAsync(first, "a.jpg");
            rejectId = await AddPhotoAsync(first, "b.jpg");
            await first.GetRequiredService<IDecisionService>().SaveAsync(
                [(keepId, new PhotoDecision(CullState.Keep, 4, ColourLabel.Green)), (rejectId, new PhotoDecision(CullState.Reject))],
                Ct);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);
        var loaded = await second.GetRequiredService<IDecisionService>().LoadAsync(Ct);

        Assert.Equal(new PhotoDecision(CullState.Keep, 4, ColourLabel.Green), loaded[keepId]);
        Assert.Equal(new PhotoDecision(CullState.Reject), loaded[rejectId]);
    }

    [Fact]
    public async Task SavingAnEmptyDecision_RemovesTheRow()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoDecisionRepository>();
        var id = await AddPhotoAsync(services, "a.jpg");
        await repository.SaveAsync([(id, PhotoDecision.Unset.WithState(CullState.Keep))], Ct);

        await repository.SaveAsync([(id, PhotoDecision.Unset)], Ct);

        Assert.Empty(await repository.GetAllAsync(Ct));
        await using var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(0, await context.PhotoDecisions.CountAsync(Ct));
    }

    [Fact]
    public async Task Update_OverwritesExistingRow()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoDecisionRepository>();
        var id = await AddPhotoAsync(services, "a.jpg");

        await repository.SaveAsync([(id, new PhotoDecision(CullState.Keep, 2))], Ct);
        await repository.SaveAsync([(id, new PhotoDecision(CullState.Review, 5, ColourLabel.Blue))], Ct);

        Assert.Equal(new PhotoDecision(CullState.Review, 5, ColourLabel.Blue), Assert.Single(await repository.GetAllAsync(Ct)).Value);
    }

    [Fact]
    public async Task Database_RejectsOutOfRangeRating()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var id = await AddPhotoAsync(services, "a.jpg");
        await using var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);

        context.PhotoDecisions.Add(new PhotoDecisionEntity { PhotoId = id, State = CullState.Keep, Rating = 9 });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task DeletingAPhoto_DeletesItsDecision()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var id = await AddPhotoAsync(services, "a.jpg");
        await services.GetRequiredService<IPhotoDecisionRepository>().SaveAsync([(id, new PhotoDecision(CullState.Keep))], Ct);

        await using (var context = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Photos.Where(p => p.Id == id).ExecuteDeleteAsync(Ct);
        }

        Assert.Empty(await services.GetRequiredService<IPhotoDecisionRepository>().GetAllAsync(Ct));
    }
}
