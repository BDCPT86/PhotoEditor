using FrameForge.Core.Editing;
using static FrameForge.Rendering.Tests.TestImages;

namespace FrameForge.Rendering.Tests;

/// <summary>
/// Visual regression guard: fixed recipes on a fixed generated image must keep producing the same pixels (±1 level,
/// allowing for floating-point differences between CPUs). If a deliberate change to the rendering maths moves these,
/// review real photos before and after (docs/visual-review-m06.md), bump <see cref="EditRenderer.Version"/>, and update
/// the expected values. Values recorded with render-v1 on 2026-11-16; render-v2 (local adjustments) leaves them unchanged.
/// </summary>
public sealed class GoldenTests
{
    private static readonly (double U, double V)[] Points = [(0, 0), (0.5, 0.5), (1, 1), (0.25, 0.75)];

    public static TheoryData<string, EditRecipe, int, int, int[]> Cases => new()
    {
        { "exposure", EditRecipe.Neutral with { Exposure = 1 }, 64, 48, [58, 45, 255, 176, 168, 174, 255, 255, 71, 114, 233, 181] },
        { "warm", EditRecipe.Neutral with { Temperature = 50, Tint = 10 }, 64, 48, [44, 29, 184, 138, 120, 116, 236, 217, 45, 89, 168, 121] },
        { "tone", EditRecipe.Neutral with { Contrast = 40, Highlights = -60, Shadows = 50 }, 64, 48, [49, 37, 236, 108, 103, 107, 179, 179, 39, 61, 131, 101] },
        { "colour", EditRecipe.Neutral with { Saturation = -50, Vibrance = 60 }, 64, 48, [50, 66, 139, 127, 122, 126, 216, 218, 136, 110, 165, 138] },
        { "geometry", EditRecipe.Neutral with { QuarterTurns = 1, Straighten = 8, Crop = new CropRect(0.1, 0.2, 0.7, 0.6) }, 29, 33, [91, 197, 120, 130, 133, 122, 170, 69, 125, 156, 156, 102] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Output_MatchesRecordedPixels(string name, EditRecipe recipe, int width, int height, int[] expected)
    {
        var output = Render(Gradient(64, 48), recipe);

        Assert.Equal((width, height), (output.Width, output.Height));
        for (var i = 0; i < Points.Length; i++)
        {
            var (u, v) = Points[i];
            var (r, g, b) = Pixel(output, (int)(u * (output.Width - 1)), (int)(v * (output.Height - 1)));
            int[] actual = [r, g, b];
            for (var c = 0; c < 3; c++)
            {
                Assert.True(Math.Abs(actual[c] - expected[(i * 3) + c]) <= 1, $"{name}: point {i} channel {c} is {actual[c]}, expected {expected[(i * 3) + c]}");
            }
        }
    }
}
