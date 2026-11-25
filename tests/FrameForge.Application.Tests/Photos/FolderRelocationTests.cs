using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Photos;

public sealed class FolderRelocationTests
{
    private const string Old = @"E:\Photos\Match";
    private const string New = @"D:\Archive\Match";
    private static readonly DateTime Modified = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PhotoFileState State(string path, long size = 1000, ImportStatus status = ImportStatus.Imported) =>
        new(Guid.NewGuid(), path, Path.GetDirectoryName(path)!, size, Modified, status);

    private static PhotoFolderScan Scan(params PhotoFileInfo[] files) => new(files, []);

    private static PhotoFileInfo File(string path, long size = 1000, DateTime? modified = null) => new(path, size, modified ?? Modified);

    [Fact]
    public void Plan_MatchesByRelativePath_IncludingSubfolders()
    {
        var library = new[] { State($@"{Old}\IMG_1.jpg"), State($@"{Old}\Day 2\IMG_2.jpg") };

        var plan = FolderRelocationPlanner.Plan(Old, New, library, Scan(File($@"{New}\IMG_1.jpg"), File($@"{New}\Day 2\IMG_2.jpg")));

        Assert.Equal(2, plan.Movable);
        Assert.Equal([$@"{New}\Day 2\IMG_2.jpg", $@"{New}\IMG_1.jpg"], plan.Items.Select(i => i.NewPath).Order());
    }

    [Fact]
    public void Plan_SaysWhyAPhotoWontMove()
    {
        var other = State($@"{New}\IMG_4.jpg");
        var library = new[]
        {
            State($@"{Old}\IMG_1.jpg"),           // found
            State($@"{Old}\IMG_2.jpg"),           // copied: modified time changed
            State($@"{Old}\IMG_3.jpg", size: 5),  // a different file with the same name
            State($@"{Old}\IMG_4.jpg"),           // the new path is already another photo
            State($@"{Old}\IMG_5.jpg"),           // not there
            other,
        };
        var scan = Scan(
            File($@"{New}\IMG_1.jpg"),
            File($@"{New}\IMG_2.jpg", modified: Modified.AddHours(3)),
            File($@"{New}\IMG_3.jpg", size: 6),
            File($@"{New}\IMG_4.jpg"));

        var plan = FolderRelocationPlanner.Plan(Old, New, library, scan);

        Assert.Equal(5, plan.Items.Count); // only photos under the old folder
        Assert.Equal(1, plan.Count(RelocationOutcome.Found));
        Assert.Equal(1, plan.Count(RelocationOutcome.ModifiedTimeDiffers));
        Assert.Equal(1, plan.Count(RelocationOutcome.DifferentSize));
        Assert.Equal(1, plan.Count(RelocationOutcome.AlreadyInLibrary));
        Assert.Equal(1, plan.Count(RelocationOutcome.NotFound));
        Assert.Equal(2, plan.Movable);
    }

    [Fact]
    public void Plan_LeavesOtherFoldersOut_EvenWithASimilarName()
    {
        var library = new[] { State($@"{Old}\IMG_1.jpg"), State($@"{Old} 2\IMG_1.jpg") };

        var plan = FolderRelocationPlanner.Plan(Old, New, library, Scan(File($@"{New}\IMG_1.jpg")));

        Assert.Single(plan.Items);
    }

    [Fact]
    public void Plan_RefusesTheSameFolder() =>
        Assert.Throws<ArgumentException>(() => FolderRelocationPlanner.Plan(Old, Old + @"\", [], Scan()));

    [Fact]
    public async Task Apply_MovesOnlyMatchedPhotos_KeepsTheirIds_AndClearsMissing()
    {
        var files = new FakeEnumerator();
        var repository = new InMemoryPhotoRepository();
        foreach (var name in new[] { "IMG_1.jpg", "IMG_2.jpg" })
        {
            files.AddFile($@"{Old}\{name}");
        }

        var import = new PhotoImportService(files, new FakeMetadataReader(), repository, new PhotoImportOptions(), TimeProvider.System, NullLogger<PhotoImportService>.Instance);
        await import.ImportFolderAsync(Old, true, null, Ct);
        await repository.MarkMissingAsync(repository.All.Select(p => p.Id).ToList(), "gone", Ct);
        var ids = repository.All.ToDictionary(p => p.FileName, p => p.Id);
        files.AddFile($@"{New}\IMG_1.jpg");
        var service = new FolderRelocationService(files, repository, NullLogger<FolderRelocationService>.Instance);

        var plan = await service.PreviewAsync(Old, New, Ct);
        var moved = await service.ApplyAsync(plan, Ct);

        Assert.Equal(1, moved);
        var photo = repository.Get($@"{New}\IMG_1.jpg");
        Assert.Equal(ids["IMG_1.jpg"], photo.Id);
        Assert.Equal(New, photo.FolderPath);
        Assert.Equal(ImportStatus.Imported, photo.Status);
        Assert.Equal(ImportStatus.Missing, repository.Get($@"{Old}\IMG_2.jpg").Status);
    }
}
