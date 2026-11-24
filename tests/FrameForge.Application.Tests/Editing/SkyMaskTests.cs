using FrameForge.Analysis;
using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;

namespace FrameForge.Application.Tests.Editing;

/// <summary>Select sky's deterministic rules on synthetic scenes.</summary>
public sealed class SkyMaskTests
{
    private const int W = 120;
    private const int H = 80;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Blue sky getting lighter towards the horizon at row 40, a green field below.</summary>
    private static (byte R, byte G, byte B) Landscape(int x, int y) =>
        y < 40 ? ((byte)(90 + y), (byte)(140 + y), (byte)(230 + (y / 4))) : ((byte)60, (byte)130, (byte)50);

    private static double Share(MaskGeneration result, Func<int, int, bool> where)
    {
        int inside = 0, total = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (where(x, y))
                {
                    total++;
                    inside += result.Mask!.Alpha[(y * W) + x] == 255 ? 1 : 0;
                }
            }
        }

        return inside / (double)total;
    }

    [Fact]
    public void BlueSkyAboveAField_IsSelected_AndTheFieldIsNot()
    {
        var result = new SkyMaskGenerator().Generate(AnalysisImage.Create(W, H, Landscape), [], Ct);

        Assert.True(result.HasMask);
        Assert.Equal(1.0, Share(result, (_, y) => y < 40));
        Assert.Equal(0.0, Share(result, (_, y) => y >= 40));
        Assert.Equal("Sky covers 50% of the photo", result.Summary);
    }

    [Fact]
    public void ADarkBuildingInTheSky_IsLeftOut()
    {
        var image = AnalysisImage.Create(W, H, (x, y) => x is >= 50 and < 70 && y >= 10 ? ((byte)70, (byte)60, (byte)55) : Landscape(x, y));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.Equal(0.0, Share(result, (x, y) => x is >= 50 and < 70 && y >= 10));
        Assert.Equal(1.0, Share(result, (x, y) => y < 40 && (x < 48 || x >= 72)));
    }

    [Fact]
    public void OvercastWhiteSky_IsSelected()
    {
        var image = AnalysisImage.Create(W, H, (_, y) => y < 30 ? ((byte)215, (byte)218, (byte)222) : ((byte)90, (byte)80, (byte)70));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.Equal(1.0, Share(result, (_, y) => y < 30));
        Assert.Equal(0.0, Share(result, (_, y) => y >= 30));
    }

    [Fact]
    public void NoSky_SaysSo()
    {
        var result = new SkyMaskGenerator().Generate(AnalysisImage.Create(W, H, (_, _) => (60, 130, 50)), [], Ct);

        Assert.False(result.HasMask);
        Assert.Equal("No sky found. Click the sky to select it.", result.Summary);
    }

    [Fact]
    public void ABirdInTheSky_IsFilled()
    {
        var image = AnalysisImage.Create(W, H, (x, y) => x is >= 30 and < 33 && y is >= 10 and < 12 ? ((byte)20, (byte)20, (byte)20) : Landscape(x, y));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.Equal(1.0, Share(result, (_, y) => y < 40));
    }

    [Fact]
    public void SkyBelowADarkTopBand_NeedsAnIncludeClick()
    {
        // A dark awning along the top edge hides the sky from the top-row seeds.
        var image = AnalysisImage.Create(W, H, (x, y) => y < 6 ? ((byte)40, (byte)35, (byte)30) : Landscape(x, y));

        Assert.False(new SkyMaskGenerator().Generate(image, [], Ct).HasMask);
        var clicked = new SkyMaskGenerator().Generate(image, [new MaskPoint(0.5, 0.25, Include: true)], Ct);
        Assert.Equal(1.0, Share(clicked, (_, y) => y is >= 6 and < 40));
    }

    [Fact]
    public void ExcludeClick_RemovesTheConnectedPartOfTheSky()
    {
        // Two areas of sky separated by a dark pillar; the click removes the right one only.
        var image = AnalysisImage.Create(W, H, (x, y) => x is >= 58 and < 62 ? ((byte)50, (byte)45, (byte)40) : Landscape(x, y));

        var result = new SkyMaskGenerator().Generate(image, [new MaskPoint(0.8, 0.2, Include: false)], Ct);

        // A corner pixel where the pillar meets the horizon touches only edge pixels, so growth can miss it.
        Assert.InRange(Share(result, (x, y) => y < 40 && x < 58), 0.995, 1.0);
        Assert.Equal(0.0, Share(result, (x, _) => x >= 62));
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        var image = AnalysisImage.Create(W, H, Landscape);

        Assert.Equal(new SkyMaskGenerator().Generate(image, [], Ct).Mask!.Alpha, new SkyMaskGenerator().Generate(image, [], Ct).Mask!.Alpha);
    }

    /// <summary>A pale painted wall with a fine texture (concrete, graffiti): sky-coloured, but not smooth like sky.</summary>
    private static (byte R, byte G, byte B) PaleWall(int x, int y)
    {
        var grain = (((x * 7) + (y * 13)) % 5 - 2) * 4;
        return ((byte)(196 + grain), (byte)(202 + grain), (byte)(210 + grain));
    }

    [Fact]
    public void APaleTexturedWallAtTheTop_IsNotSky()
    {
        // Rules v2 (B7): walls like this were taken as sky on the user's photos.
        var image = AnalysisImage.Create(W, H, (x, y) => y < 30 ? PaleWall(x, y) : ((byte)60, (byte)130, (byte)50));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.False(result.HasMask);
    }

    [Fact]
    public void AShadedBluePanelAtTheTop_IsNotSky()
    {
        var image = AnalysisImage.Create(W, H, (_, y) => y < 30 ? ((byte)60, (byte)90, (byte)140) : ((byte)60, (byte)130, (byte)50));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.False(result.HasMask);
    }

    [Fact]
    public void SkyBesideAWall_KeepsTheSky_AndDropsTheWall()
    {
        // Sky on the left, a pale wall on the right, a dark pole between them.
        var image = AnalysisImage.Create(W, H, (x, y) => y >= 40 ? ((byte)60, (byte)130, (byte)50) : x < 58 ? Landscape(x, y) : x < 62 ? ((byte)40, (byte)40, (byte)40) : PaleWall(x, y));

        var result = new SkyMaskGenerator().Generate(image, [], Ct);

        Assert.Equal(1.0, Share(result, (x, y) => y < 40 && x < 56));   // clear of the pole's edge
        Assert.Equal(0.0, Share(result, (x, y) => y < 40 && x >= 62));
    }

    [Fact]
    public void AnIncludeClick_KeepsAPartTheRulesWouldDrop()
    {
        var image = AnalysisImage.Create(W, H, (x, y) => y < 30 ? PaleWall(x, y) : ((byte)60, (byte)130, (byte)50));

        var result = new SkyMaskGenerator().Generate(image, [new MaskPoint(0.5, 0.1, true)], Ct);

        Assert.True(result.HasMask);
        Assert.True(Share(result, (_, y) => y < 30) > 0.5);
    }
}
