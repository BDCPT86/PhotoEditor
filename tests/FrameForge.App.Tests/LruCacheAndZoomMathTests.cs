using System.Windows;
using FrameForge.App.Controls;
using FrameForge.App.Services;

namespace FrameForge.App.Tests;

public sealed class LruCacheAndZoomMathTests
{
    [Fact]
    public void Lru_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(2, "two");
        Assert.True(cache.TryGet(1, out _)); // 1 is now most recent

        cache.Set(3, "three");

        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(1, out var one));
        Assert.Equal("one", one);
        Assert.True(cache.TryGet(3, out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Lru_OverwriteDoesNotGrow()
    {
        var cache = new LruCache<int, string>(2);
        cache.Set(1, "a");
        cache.Set(1, "b");

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(1, out var value));
        Assert.Equal("b", value);
    }

    [Theory]
    [InlineData(6000, 4000, 1200, 800, 1.0, 0.2)]   // limited by both
    [InlineData(6000, 4000, 1200, 2000, 1.0, 0.2)]  // limited by width
    [InlineData(6000, 4000, 3000, 400, 1.0, 0.1)]   // limited by height
    [InlineData(6000, 4000, 1200, 800, 1.5, 0.3)]   // 150% display scaling: more device pixels
    [InlineData(400, 300, 1200, 800, 1.0, 1.0)]     // small image: never enlarged past 100%
    public void FitZoom(double imgW, double imgH, double viewW, double viewH, double dpi, double expected) =>
        Assert.Equal(expected, ZoomMath.FitZoom(imgW, imgH, viewW, viewH, dpi), precision: 6);

    [Fact]
    public void ClampZoom_BetweenFitAndMax()
    {
        Assert.Equal(0.25, ZoomMath.ClampZoom(0.1, fitZoom: 0.25));
        Assert.Equal(ZoomMath.MaxZoom, ZoomMath.ClampZoom(100, fitZoom: 0.25));
        Assert.Equal(2, ZoomMath.ClampZoom(2, fitZoom: 0.25));
    }

    [Fact]
    public void ZoomAt_KeepsAnchorPointFixed()
    {
        var offset = new Point(-100, -50);
        var anchor = new Point(300, 200);
        const double oldScale = 0.5;
        const double newScale = 2.0;

        // Image coordinate under the anchor before zooming...
        var imagePoint = new Point((anchor.X - offset.X) / oldScale, (anchor.Y - offset.Y) / oldScale);
        var newOffset = ZoomMath.ZoomAt(offset, anchor, oldScale, newScale);

        // ...is still under the anchor afterwards.
        Assert.Equal(anchor.X, newOffset.X + (imagePoint.X * newScale), precision: 6);
        Assert.Equal(anchor.Y, newOffset.Y + (imagePoint.Y * newScale), precision: 6);
    }

    [Fact]
    public void ClampOffset_CentresSmallContent_AndKeepsLargeContentCovering()
    {
        var viewport = new Size(1000, 800);

        var centred = ZoomMath.ClampOffset(new Point(500, -500), new Size(400, 200), viewport);
        Assert.Equal(new Point(300, 300), centred);

        var large = new Size(3000, 2000);
        Assert.Equal(new Point(0, 0), ZoomMath.ClampOffset(new Point(50, 50), large, viewport));
        Assert.Equal(new Point(-2000, -1200), ZoomMath.ClampOffset(new Point(-9999, -9999), large, viewport));
    }
}
