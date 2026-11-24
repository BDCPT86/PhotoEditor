using System.Text;
using FrameForge.Evaluation.Sheets;

namespace FrameForge.Evaluation.Tests;

/// <summary>B7: the sky grading sheet and the hand-drawn mask measures.</summary>
public sealed class SkyAndDrawSheetTests
{
    private static SkyItem Sky(string id, bool found) => new(id, id + ".jpg", found, found ? "Sky covers 20%" : "No sky found.", found ? 0.2 : 0, 300, "sky-rules@1");

    [Fact]
    public void SkyScore_CountsFalseSkyAndMisses()
    {
        var sheet = new JudgingSheets.SheetFile("mask-sky", "sky-test", DateTimeOffset.Now, "x", 0, null, null,
            Sky: [Sky("s001", true), Sky("s002", true), Sky("s003", true), Sky("s004", false), Sky("s005", false)]);
        var answers = new Dictionary<string, string[]>
        {
            ["s001"] = ["good"],
            ["s002"] = ["nosky"],
            ["s003"] = ["usable"],
            ["s004"] = ["right"],
            ["s005"] = ["missed"],
        };
        var report = new StringBuilder();

        SkySheet.Score(sheet, answers, report);

        var text = report.ToString();
        Assert.Contains("**Sky found (3):** good 1 · usable 1 · bad 0 · no real sky 1", text, StringComparison.Ordinal);
        Assert.Contains("**False sky** (found on a photo with no real sky): 1 of 3", text, StringComparison.Ordinal);
        Assert.Contains("**No sky found (2):** right 1 · missed visible sky 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SkyScore_RejectsAnAnswerThatDoesNotFitThePhoto()
    {
        var sheet = new JudgingSheets.SheetFile("mask-sky", "sky-test", DateTimeOffset.Now, "x", 0, null, null, Sky: [Sky("s001", false)]);

        Assert.Throws<FormatException>(() => SkySheet.Score(sheet, new Dictionary<string, string[]> { ["s001"] = ["good"] }, new StringBuilder()));
    }

    [Fact]
    public void Rasterize_FillsAddShapes_AndCutsHoles()
    {
        // A square over the middle half, with a cut through its centre quarter, on a 8 × 8 grid.
        var shapes = new List<DrawnShape>
        {
            new("add", [[0.25, 0.25], [0.75, 0.25], [0.75, 0.75], [0.25, 0.75]]),
            new("cut", [[0.375, 0.375], [0.625, 0.375], [0.625, 0.625], [0.375, 0.625]]),
        };

        var mask = DrawSheet.Rasterize(shapes, 8, 8);

        Assert.Equal(16 - 4, mask.Count(v => v));
        Assert.True(mask[(2 * 8) + 2]);    // inside the square
        Assert.False(mask[(4 * 8) + 4]);   // in the cut
        Assert.False(mask[0]);
    }

    [Fact]
    public void Iou_IsSharedOverCombined()
    {
        bool[] a = [true, true, false, false], b = [false, true, true, false];

        Assert.Equal(1 / 3d, DrawSheet.Iou(a, b), 6);
        Assert.Equal(1, DrawSheet.Iou(new bool[4], new bool[4]));
    }

    [Fact]
    public void EdgeF_IsOneForTheSameMask_AndDropsWhenEdgesAreFarApart()
    {
        const int size = 40;
        static bool[] Square(int from, int to)
        {
            var m = new bool[size * size];
            for (var y = from; y < to; y++)
            {
                for (var x = from; x < to; x++)
                {
                    m[(y * size) + x] = true;
                }
            }

            return m;
        }

        Assert.Equal(1, DrawSheet.EdgeF(Square(10, 30), Square(10, 30), size, size, 3));
        Assert.Equal(1, DrawSheet.EdgeF(Square(10, 30), Square(12, 30), size, size, 3));   // within the tolerance
        Assert.True(DrawSheet.EdgeF(Square(5, 35), Square(15, 25), size, size, 3) < 0.01);
    }

    [Fact]
    public void Inside_IsTheShareOfTheAppMaskWithinTheOutline()
    {
        bool[] app = [true, true, true, true, false], mine = [true, true, true, false, true];

        Assert.Equal(0.75, DrawSheet.Inside(app, mine));
        Assert.Equal(1, DrawSheet.Inside(new bool[3], mine[..3]));
    }
}
