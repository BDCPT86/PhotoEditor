using FrameForge.Application.Editing.Masks;
using FrameForge.Infrastructure.Editing;
using FrameForge.Infrastructure.Tests.TestSupport;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Editing;

public sealed class DiskMaskCacheTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly TempDirectory _temp = new();
    private readonly DiskMaskCache _cache;
    private readonly AppDataPaths _paths;

    public DiskMaskCacheTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _cache = new DiskMaskCache(_paths, NullLogger<DiskMaskCache>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private static CachedMask Sample()
    {
        var alpha = new byte[300 * 200];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = (byte)(i % 300 < 150 ? 255 : i % 7);
        }

        return new CachedMask(new MaskImage(300, 200, alpha), "2 people (1 click)");
    }

    [Fact]
    public void Mask_RoundTripsExactly_UnderTheMasksFolder()
    {
        var mask = Sample();

        _cache.Save(Key, mask);
        var loaded = _cache.TryGet(Key);

        Assert.NotNull(loaded);
        Assert.Equal((300, 200), (loaded.Mask.Width, loaded.Mask.Height));
        Assert.Equal(mask.Mask.Alpha, loaded.Mask.Alpha);
        Assert.Equal(mask.Summary, loaded.Summary);
        var file = Assert.Single(Directory.GetFiles(_paths.MasksDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(_paths.MasksDirectory, "01", Key + ".mask"), file); // no temp file left behind
    }

    [Fact]
    public void MissingEntry_IsNull() => Assert.Null(_cache.TryGet(Key));

    [Theory]
    [InlineData(0)]   // empty
    [InlineData(5)]   // header cut short
    [InlineData(40)]  // mask data cut short
    public void DamagedFile_IsTreatedAsMissing_AndDeleted(int keep)
    {
        _cache.Save(Key, Sample());
        var path = _cache.PathFor(Key);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..Math.Min(keep, bytes.Length)]);

        Assert.Null(_cache.TryGet(Key));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NotAMaskFile_IsTreatedAsMissing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cache.PathFor(Key))!);
        File.WriteAllText(_cache.PathFor(Key), "hello, this is not a mask");

        Assert.Null(_cache.TryGet(Key));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("ABCDEF0123456789")]
    [InlineData("abc")]
    public void KeysThatAreNotLowerCaseHex_AreRefused(string key) =>
        Assert.Throws<ArgumentException>(() => _cache.PathFor(key));
}
