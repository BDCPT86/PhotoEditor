using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Photos;

/// <summary>Import use case over the real file system and real SQLite database (imaging stubbed).</summary>
public sealed class ImportIntegrationTests : IDisposable
{
    private readonly TempDirectory _data = new();
    private readonly TempDirectory _photos = new();
    private readonly AppDataPaths _paths;

    public ImportIntegrationTests()
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

    private string WriteFile(string relativePath, string content = "JPEG")
    {
        var path = Path.Combine(_photos.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ImportTwice_ProducesNoDuplicateRows()
    {
        for (var i = 0; i < 60; i++)
        {
            WriteFile($@"day{i % 3}\IMG_{i:000}.jpg");
        }

        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var importer = services.GetRequiredService<IPhotoImportService>();

        var first = await importer.ImportFolderAsync(_photos.Path, true, null, Ct);
        var second = await importer.ImportFolderAsync(_photos.Path, true, null, Ct);
        var viaUpperCase = await importer.ImportFolderAsync(_photos.Path.ToUpperInvariant(), true, null, Ct);

        Assert.Equal(60, first.Added);
        Assert.Equal(60, second.Unchanged);
        Assert.Equal(60, viaUpperCase.Unchanged);
        var all = await services.GetRequiredService<IPhotoLibraryService>().GetPhotosAsync(Ct);
        Assert.Equal(60, all.Count);
        Assert.Equal(60, all.Select(p => p.FilePath.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public async Task CorruptAndDeletedFiles_AreRecordedNotDropped()
    {
        WriteFile("good.jpg");
        var corrupt = WriteFile("bad.jpg", "BAD data");
        var doomed = WriteFile("doomed.jpg");

        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var importer = services.GetRequiredService<IPhotoImportService>();
        await importer.ImportFolderAsync(_photos.Path, true, null, Ct);

        File.Delete(doomed);
        var summary = await importer.ImportFolderAsync(_photos.Path, true, null, Ct);

        Assert.Equal(1, summary.MarkedMissing);
        var byPath = (await services.GetRequiredService<IPhotoLibraryService>().GetPhotosAsync(Ct)).ToDictionary(p => p.FilePath);
        Assert.Equal(3, byPath.Count);
        Assert.Equal(ImportStatus.Failed, byPath[corrupt].Status);
        Assert.Equal(ImportStatus.Missing, byPath[doomed].Status);
        Assert.Equal(ImportStatus.Imported, byPath[Path.Combine(_photos.Path, "good.jpg")].Status);
    }

    [Fact]
    public async Task ModifiedFile_IsUpdatedInPlace()
    {
        var path = WriteFile("a.jpg");
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var importer = services.GetRequiredService<IPhotoImportService>();
        await importer.ImportFolderAsync(_photos.Path, true, null, Ct);

        File.WriteAllText(path, "JPEG but longer now");
        var summary = await importer.ImportFolderAsync(_photos.Path, true, null, Ct);

        Assert.Equal(1, summary.Updated);
        var stored = Assert.Single(await services.GetRequiredService<IPhotoLibraryService>().GetPhotosAsync(Ct));
        Assert.Equal(new FileInfo(path).Length, stored.FileSizeBytes);
    }
}
