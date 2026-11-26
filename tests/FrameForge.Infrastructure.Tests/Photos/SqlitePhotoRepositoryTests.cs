using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Photos;

public sealed class SqlitePhotoRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;

    public SqlitePhotoRepositoryTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private static Photo CreatePhoto(string path, ImportStatus status = ImportStatus.Imported) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = Path.GetDirectoryName(path)!,
        FileSizeBytes = 100,
        LastModifiedUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        ImportedUtc = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
        Status = status,
        CaptureLocalTime = new DateTime(2025, 12, 24, 18, 30, 0, DateTimeKind.Unspecified),
        Iso = 400,
        FNumber = 2.8,
        ExposureTime = "1/250",
    };

    [Fact]
    public async Task Upsert_InsertsThenUpdates_MatchingPathCaseInsensitively()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        var original = CreatePhoto(@"C:\Shoot\IMG_0001.JPG");
        await repository.UpsertAsync([original], Ct);

        var update = CreatePhoto(@"c:\shoot\img_0001.jpg");
        update.Iso = 800;
        await repository.UpsertAsync([update], Ct);

        var stored = Assert.Single(await repository.GetAllAsync(Ct));
        Assert.Equal(original.Id, stored.Id);              // identity kept
        Assert.Equal(original.ImportedUtc, stored.ImportedUtc); // first-import time kept
        Assert.Equal(800, stored.Iso);
    }

    [Fact]
    public async Task Remove_TakesThePhotosDecisionEditAndAnalysisWithIt_AndLeavesOthersAlone()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var photos = services.GetRequiredService<IPhotoRepository>();
        var decisions = services.GetRequiredService<Application.Decisions.IPhotoDecisionRepository>();
        var edits = services.GetRequiredService<Application.Editing.IEditRepository>();
        var analysis = services.GetRequiredService<Application.Analysis.IAnalysisResultRepository>();
        var gone = CreatePhoto(@"C:\Shoot\IMG_0001.JPG");
        var kept = CreatePhoto(@"C:\Shoot\IMG_0002.JPG");
        await photos.UpsertAsync([gone, kept], Ct);
        foreach (var photo in new[] { gone, kept })
        {
            await decisions.SaveAsync([(photo.Id, new Core.Decisions.PhotoDecision(Core.Decisions.CullState.Reject))], Ct);
            await edits.SaveAsync([(photo.Id, Core.Editing.EditRecipe.Neutral with { Exposure = 1 })], Ct);
            await analysis.SaveAsync(
                [new Application.Analysis.StoredAnalysis(photo.Id, Application.Analysis.AnalysisKind.ContentHash, "1", "s", 100, photo.LastModifiedUtc, "\"abc\"", photo.ImportedUtc)],
                Ct);
        }

        var removed = await photos.RemoveAsync([gone.Id, Guid.NewGuid()], Ct);

        Assert.Equal(1, removed);
        Assert.Equal(kept.Id, Assert.Single(await photos.GetAllAsync(Ct)).Id);
        Assert.Equal([kept.Id], (await decisions.GetAllAsync(Ct)).Keys);
        Assert.Equal([kept.Id], (await edits.GetAllAsync(Ct)).Recipes.Keys);
        Assert.Equal([kept.Id], (await analysis.GetAllAsync(Ct)).Select(a => a.PhotoId).Distinct());
    }

    [Fact]
    public async Task Remove_WithNothingToRemove_DoesNothing()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var photos = services.GetRequiredService<IPhotoRepository>();

        Assert.Equal(0, await photos.RemoveAsync([], Ct));
    }

    [Fact]
    public async Task RoundTripsMetadata_AndTimeKinds()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        var photo = CreatePhoto(@"C:\Shoot\a.jpg");
        await repository.UpsertAsync([photo], Ct);

        var stored = Assert.Single(await repository.GetAllAsync(Ct));

        Assert.Equal(photo.LastModifiedUtc, stored.LastModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, stored.LastModifiedUtc.Kind);
        Assert.Equal(photo.CaptureLocalTime, stored.CaptureLocalTime);
        Assert.Equal(DateTimeKind.Unspecified, stored.CaptureLocalTime!.Value.Kind); // camera local time, not UTC
        Assert.Equal(2.8, stored.FNumber);
        Assert.Equal("1/250", stored.ExposureTime);
        Assert.Equal(ImportStatus.Imported, stored.Status);
    }

    [Fact]
    public async Task MarkMissing_UpdatesOnlyGivenPhotos()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        var gone = CreatePhoto(@"C:\Shoot\gone.jpg");
        var kept = CreatePhoto(@"C:\Shoot\kept.jpg");
        await repository.UpsertAsync([gone, kept], Ct);

        await repository.MarkMissingAsync([gone.Id], "not found", Ct);

        var states = (await repository.GetFileStatesAsync(Ct)).ToDictionary(s => s.FilePath);
        Assert.Equal(ImportStatus.Missing, states[gone.FilePath].Status);
        Assert.Equal(ImportStatus.Imported, states[kept.FilePath].Status);
    }

    [Fact]
    public async Task GetAll_OrdersByFolderThenFileName()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        await repository.UpsertAsync(
            [CreatePhoto(@"C:\B\1.jpg"), CreatePhoto(@"C:\A\2.jpg"), CreatePhoto(@"C:\A\1.jpg")],
            Ct);

        var paths = (await repository.GetAllAsync(Ct)).Select(p => p.FilePath);

        Assert.Equal([@"C:\A\1.jpg", @"C:\A\2.jpg", @"C:\B\1.jpg"], paths);
    }

    [Fact]
    public async Task Relocate_MovesPaths_KeepsIdsAndDecisions_AndClearsMissing()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        var photo = CreatePhoto(@"E:\Match\Day 2\IMG_1.jpg", ImportStatus.Missing);
        await repository.UpsertAsync([photo], Ct);
        var decisions = services.GetRequiredService<FrameForge.Application.Decisions.IDecisionService>();
        await decisions.SaveAsync([(photo.Id, new FrameForge.Core.Decisions.PhotoDecision(FrameForge.Core.Decisions.CullState.Keep, 5))], Ct);

        var moved = await repository.RelocateAsync([new PhotoMove(photo.Id, @"D:\Archive\Match\Day 2\IMG_1.jpg")], Ct);

        Assert.Equal(1, moved);
        var stored = Assert.Single(await repository.GetAllAsync(Ct));
        Assert.Equal(photo.Id, stored.Id);
        Assert.Equal(@"D:\Archive\Match\Day 2", stored.FolderPath);
        Assert.Equal("IMG_1.jpg", stored.FileName);
        Assert.Equal(ImportStatus.Imported, stored.Status);
        Assert.Null(stored.LastError);
        Assert.Equal(5, (await decisions.LoadAsync(Ct))[photo.Id].Rating);
    }

    [Fact]
    public async Task Relocate_IsAllOrNothing_WhenOnePathClashes()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var repository = services.GetRequiredService<IPhotoRepository>();
        var a = CreatePhoto(@"E:\Match\1.jpg");
        var b = CreatePhoto(@"E:\Match\2.jpg");
        var other = CreatePhoto(@"D:\Match\2.jpg");
        await repository.UpsertAsync([a, b, other], Ct);

        await Assert.ThrowsAnyAsync<Exception>(() => repository.RelocateAsync(
            [new PhotoMove(a.Id, @"D:\Match\1.jpg"), new PhotoMove(b.Id, @"D:\Match\2.jpg")], Ct));

        var paths = (await repository.GetAllAsync(Ct)).Select(p => p.FilePath).Order();
        Assert.Equal([@"D:\Match\2.jpg", @"E:\Match\1.jpg", @"E:\Match\2.jpg"], paths);
    }
}
