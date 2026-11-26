using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

/// <summary>M09 C1: the histogram follows the image on screen, off the UI thread, in the viewer (H) and in Develop.</summary>
public sealed class HistogramWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnalysisImage Solid(int width, int height, byte value) =>
        new(width, height, Enumerable.Repeat(value, width * height * 4).ToArray());

    private static BitmapSource White(int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, Enumerable.Repeat((byte)255, width * height).ToArray(), width);
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(condition());
    }

    // ------------------------------------------------------------------ the view model

    [Fact]
    public async Task Counts_RenderedPixels_AndFrozenBitmaps()
    {
        var histogram = new HistogramViewModel(NullLogger.Instance);

        histogram.Show(Solid(10, 10, 0));
        await histogram.Work;
        Assert.Equal(100, histogram.Histogram!.PixelCount);
        Assert.Equal("Shadows 100 %", histogram.ShadowText);
        Assert.Equal("Highlights 0 %", histogram.HighlightText);

        histogram.Show(White(4, 5)); // Gray8, converted to BGRA off the UI thread
        await histogram.Work;
        Assert.Equal(20, histogram.Histogram!.Luminance[255]);
        Assert.Contains("highlights 100 %", histogram.AccessibleText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImagesArrivingWhileCounting_ReplaceEachOther_AndTheLatestWins()
    {
        var histogram = new HistogramViewModel(NullLogger.Instance);
        var images = Enumerable.Range(0, 20).Select(i => Solid(400, 300, (byte)(i * 10))).ToList();

        foreach (var image in images)
        {
            histogram.Show(image); // as fast as slider drags arrive
        }

        await WaitUntil(() => histogram.Histogram?.Luminance[190] == 400 * 300);
    }

    [Fact]
    public async Task Clearing_DropsTheHistogram()
    {
        var histogram = new HistogramViewModel(NullLogger.Instance);
        histogram.Show(Solid(2, 2, 50));
        await histogram.Work;

        histogram.Show(null);

        Assert.Null(histogram.Histogram);
        Assert.Null(histogram.ShadowText);
        Assert.Equal("Histogram: no image", histogram.AccessibleText);
    }

    [Theory]
    [InlineData(0.0, "0 %")]
    [InlineData(0.04, "< 0{0}1 %")]
    [InlineData(1.44, "1{0}4 %")]
    [InlineData(12.6, "13 %")]
    public void Shares_ReadNaturally(double percent, string expected) =>
        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
            HistogramViewModel.Share(percent));

    [Fact]
    public void TheDrawnCurve_SmoothsACombIntoAShape_AndScalesToItsPeak()
    {
        // Every other level empty, as an exposure gain leaves an 8-bit image.
        var comb = Enumerable.Range(0, ImageHistogram.Bins).Select(i => i % 2 == 0 ? 100 : 0).ToArray();

        var smoothed = Controls.HistogramView.Smooth(comb);

        Assert.All(smoothed.Skip(1).Take(250), v => Assert.Equal(50, v));
        Assert.Equal(50, Controls.HistogramView.ScaleOf([smoothed]));
    }

    // ------------------------------------------------------------------ in the viewer and Develop

    [Fact]
    public async Task H_ShowsItOverThePhoto_InTheViewerOnly_AndIsRemembered()
    {
        using var f = new CullingFixture(photoCount: 2);
        await f.LoadAsync();
        Assert.False(f.Library.TryHandle(CullingAction.ToggleHistogram)); // the grid has no histogram

        f.Library.OpenViewerCommand.Execute(f.Library.Photos[0]);
        await WaitUntil(() => f.Viewer.Image is not null);
        Assert.Null(f.Viewer.Histogram.Histogram); // hidden: nothing counted

        Assert.True(f.Library.TryHandle(CullingAction.ToggleHistogram));

        Assert.True(f.Viewer.ShowFloatingHistogram);
        await WaitUntil(() => f.Viewer.Histogram.Histogram is not null);
        Assert.Equal(f.Viewer.Image!.PixelWidth * f.Viewer.Image.PixelHeight, f.Viewer.Histogram.Histogram!.PixelCount);
        await WaitUntil(() => f.SettingsStore.GetValueJsonAsync(AppSettings.ViewerShowHistogram.Key, Ct).Result == "true");
    }

    [Fact]
    public async Task TheRememberedChoice_IsUsedNextTime()
    {
        using var f = new CullingFixture(photoCount: 1);
        await f.Settings.SetAsync(AppSettings.ViewerShowHistogram, true, Ct);

        await f.LoadAsync();

        Assert.True(f.Viewer.ShowHistogram);
    }

    [Fact]
    public async Task InDevelop_ItCountsTheRenderedEdit_AndTheFloatingOneHides()
    {
        using var f = new CullingFixture(photoCount: 1);
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.ToggleDevelop);
        await WaitUntil(() => f.Viewer.Develop.IsOpen);
        await WaitUntil(() => f.Viewer.Develop.RenderedPixels is not null);
        await f.Viewer.Develop.RenderLoop;

        f.Library.TryHandle(CullingAction.ToggleHistogram);

        Assert.False(f.Viewer.ShowFloatingHistogram); // Develop shows its own
        var rendered = f.Viewer.Develop.RenderedPixels!;
        await WaitUntil(() => f.Viewer.Histogram.Histogram?.PixelCount == rendered.Width * rendered.Height);

        // An edit re-renders, and the histogram follows: it matches a reference count of the new rendered pixels.
        f.Viewer.Develop.Exposure = 2;
        await f.Viewer.Develop.FlushAsync();
        await WaitUntil(() => !ReferenceEquals(f.Viewer.Develop.RenderedPixels, rendered));
        await f.Viewer.Develop.RenderLoop;
        var after = f.Viewer.Develop.RenderedPixels!;
        var reference = ImageHistogram.Compute(after.Bgra, after.Width, after.Height, after.Width * 4);
        await WaitUntil(() => f.Viewer.Histogram.Histogram is { } h && h.Luminance.SequenceEqual(reference.Luminance) && h.Red.SequenceEqual(reference.Red));
    }
}
