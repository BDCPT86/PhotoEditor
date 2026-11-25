using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests;

public sealed class DiskCacheLimitTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;
    private readonly DiskCacheLimit _limit;

    public DiskCacheLimitTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _limit = new DiskCacheLimit(_paths, new ManualTimeProvider(Now), NullLogger<DiskCacheLimit>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>A cache file of <paramref name="kb"/> KB written <paramref name="hoursAgo"/> hours ago.</summary>
    private static string Cached(string directory, string name, int kb, double hoursAgo)
    {
        var path = Path.Combine(directory, name[..2], name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[kb * 1024]);
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime.AddHours(-hoursAgo));
        return path;
    }

    [Fact]
    public void UnderTheLimit_NothingIsDeleted()
    {
        var thumb = Cached(_paths.ThumbnailsDirectory, "aa01.jpg", 10, 100);
        var mask = Cached(_paths.MasksDirectory, "bb01.mask", 10, 100);

        var result = _limit.Trim(limitBytes: 100 * 1024, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(20 * 1024, result.BytesBefore);
        Assert.True(File.Exists(thumb));
        Assert.True(File.Exists(mask));
    }

    [Fact]
    public void OverTheLimit_TheOldestGoFirst_FromBothCaches_DownTo80Percent()
    {
        var oldest = Cached(_paths.MasksDirectory, "cc01.mask", 40, 300);
        var older = Cached(_paths.ThumbnailsDirectory, "aa01.jpg", 40, 200);
        var newer = Cached(_paths.ThumbnailsDirectory, "aa02.jpg", 40, 100);
        var newest = Cached(_paths.MasksDirectory, "cc02.mask", 40, 1);

        var result = _limit.Trim(limitBytes: 120 * 1024, TestContext.Current.CancellationToken); // 160 KB > 120; target 96

        Assert.Equal(2, result.FilesDeleted);
        Assert.Equal(80 * 1024, result.BytesAfter);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(newer));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void TemporaryFilesBeingWritten_AreLeftAlone_ButOldOnesAreGarbage()
    {
        var writing = Cached(_paths.ThumbnailsDirectory, "aa01.jpg.123.tmp", 100, 0.1);
        var abandoned = Cached(_paths.ThumbnailsDirectory, "aa02.jpg.456.tmp", 100, 48);

        var result = _limit.Trim(limitBytes: 50 * 1024, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(writing));
        Assert.False(File.Exists(abandoned));
        Assert.Equal(1, result.FilesDeleted);
    }

    [Fact]
    public void NoCacheFoldersYet_IsFine()
    {
        var result = _limit.Trim(limitBytes: 1024, TestContext.Current.CancellationToken);

        Assert.Equal(new CacheTrimResult(0, 0, 0), result);
    }
}
