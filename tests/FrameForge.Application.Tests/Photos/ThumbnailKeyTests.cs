using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Photos;

public sealed class ThumbnailKeyTests
{
    private static Photo CreatePhoto(string path = @"C:\a\b.jpg", long size = 100, long ticks = 1) => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = Path.GetDirectoryName(path)!,
        FileSizeBytes = size,
        LastModifiedUtc = new DateTime(ticks, DateTimeKind.Utc),
    };

    [Fact]
    public void IsStable_ForSameInputs() =>
        Assert.Equal(ThumbnailKey.Compute(CreatePhoto(), 320, "v1"), ThumbnailKey.Compute(CreatePhoto(), 320, "v1"));

    [Fact]
    public void IgnoresPathCasing() =>
        Assert.Equal(ThumbnailKey.Compute(CreatePhoto(@"C:\A\B.JPG"), 320, "v1"), ThumbnailKey.Compute(CreatePhoto(@"c:\a\b.jpg"), 320, "v1"));

    [Fact]
    public void Changes_WhenSourceOrSettingsChange()
    {
        var baseline = ThumbnailKey.Compute(CreatePhoto(), 320, "v1");

        Assert.NotEqual(baseline, ThumbnailKey.Compute(CreatePhoto(size: 101), 320, "v1"));
        Assert.NotEqual(baseline, ThumbnailKey.Compute(CreatePhoto(ticks: 2), 320, "v1"));
        Assert.NotEqual(baseline, ThumbnailKey.Compute(CreatePhoto(path: @"C:\a\c.jpg"), 320, "v1"));
        Assert.NotEqual(baseline, ThumbnailKey.Compute(CreatePhoto(), 256, "v1"));
        Assert.NotEqual(baseline, ThumbnailKey.Compute(CreatePhoto(), 320, "v2"));
    }

    [Fact]
    public void IsFileNameSafeHex() =>
        Assert.Matches("^[0-9a-f]{32}$", ThumbnailKey.Compute(CreatePhoto(), 320, "v1"));
}
