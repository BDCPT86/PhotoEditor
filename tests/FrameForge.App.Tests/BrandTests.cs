using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace FrameForge.App.Tests;

/// <summary>The logo source and the assets rendered from it (docs/brand.md).</summary>
public sealed class BrandTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FrameForge.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    [Fact]
    public void TheLogo_LoadsOnItsOwn_SoTheAssetToolCanRenderIt()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var stream = File.OpenRead(RepoFile("src", "FrameForge.App", "Themes", "Brand.xaml"));
                var brand = (ResourceDictionary)XamlReader.Load(stream);
                foreach (var key in MarkKeys)
                {
                    var image = Assert.IsType<DrawingImage>(brand[key]);
                    Assert.Equal(64, image.Width, 3); // the 64-unit grid the icon sizes are based on
                    Assert.Equal(64, image.Height, 3);
                }
            }
#pragma warning disable CA1031 // rethrown on the test thread below
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA); // WPF objects need an STA thread
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("Loading Brand.xaml failed: " + failure.Message, failure);
        }
    }

    private static readonly string[] MarkKeys = ["Brand.Mark", "Brand.MarkBare"];

    [Fact]
    public void TheAppIcon_HoldsEverySizeWindowsAsksFor()
    {
        var ico = File.ReadAllBytes(RepoFile("src", "FrameForge.App", "Assets", "FrameForge.ico"));

        Assert.Equal(1, BitConverter.ToUInt16(ico, 2)); // an icon
        var count = BitConverter.ToUInt16(ico, 4);
        var sizes = Enumerable.Range(0, count).Select(i => ico[6 + (16 * i)] is 0 ? 256 : ico[6 + (16 * i)]).ToList();
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], sizes);
    }
}
