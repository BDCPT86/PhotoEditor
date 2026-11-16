using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Photos;
using FrameForge.Infrastructure.Tests.TestSupport;

namespace FrameForge.Infrastructure.Tests.Photos;

public sealed class DiskThumbnailServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;
    private readonly StubThumbnailGenerator _generator = new();
    private readonly DiskThumbnailService _service;
    private readonly Photo _photo;

    public DiskThumbnailServiceTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
        _service = new DiskThumbnailService(_generator, _paths);

        var source = Path.Combine(_temp.Path, "source.jpg");
        File.WriteAllText(source, "JPEG");
        _photo = new Photo
        {
            FilePath = source,
            FileName = "source.jpg",
            FolderPath = _temp.Path,
            FileSizeBytes = 4,
            LastModifiedUtc = File.GetLastWriteTimeUtc(source),
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task GeneratesOnce_ThenServesFromDisk()
    {
        var first = await _service.GetOrCreateAsync(_photo, Ct);
        var second = await _service.GetOrCreateAsync(_photo, Ct);

        Assert.Equal(first, second);
        Assert.True(File.Exists(first));
        Assert.StartsWith(_paths.ThumbnailsDirectory, first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, _generator.Calls);
    }

    [Fact]
    public async Task RegeneratesWhenCachedFileIsDeleted()
    {
        var path = await _service.GetOrCreateAsync(_photo, Ct);
        File.Delete(path);

        var again = await _service.GetOrCreateAsync(_photo, Ct);

        Assert.Equal(path, again);
        Assert.True(File.Exists(again));
        Assert.Equal(2, _generator.Calls);
    }

    [Fact]
    public async Task RegeneratesWhenWholeCacheFolderIsDeleted()
    {
        await _service.GetOrCreateAsync(_photo, Ct);
        Directory.Delete(_paths.ThumbnailsDirectory, recursive: true);

        var again = await _service.GetOrCreateAsync(_photo, Ct);

        Assert.True(File.Exists(again));
    }

    [Fact]
    public async Task SourceChange_ProducesNewCacheEntry()
    {
        var before = await _service.GetOrCreateAsync(_photo, Ct);
        _photo.FileSizeBytes = 999;

        var after = await _service.GetOrCreateAsync(_photo, Ct);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task MissingSource_Throws_AndLeavesNoTempFiles()
    {
        _photo.FilePath = Path.Combine(_temp.Path, "gone.jpg");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _service.GetOrCreateAsync(_photo, Ct));

        Assert.Empty(Directory.EnumerateFiles(_paths.ThumbnailsDirectory, "*", SearchOption.AllDirectories));
    }
}
