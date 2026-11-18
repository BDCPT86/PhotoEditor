using System.Text;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Evaluation.Masks;
using FrameForge.Evaluation.Sheets;

namespace FrameForge.Evaluation.Tests;

/// <summary>The pure parts of the B1 mask spike: prompts, model input sizes, mask post-processing and grading.</summary>
public sealed class MaskSpikeTests
{
    private static FaceFinding Face(double x, double y, double w, double h) => new((float)x, (float)y, (float)w, (float)h, 0.9f, 100);

    // ------------------------------------------------------------------ prompts

    [Fact]
    public void BodyBox_ReachesBelowTheFace_AndAroundIt()
    {
        // A 40 px tall face at (500, 100) in a 1000 × 1000 image.
        var box = MaskMath.BodyBox(Face(0.49, 0.1, 0.02, 0.04), 1000, 1000);

        Assert.Equal(500 - 100, box.Left, 3);
        Assert.Equal(500 + 100, box.Right, 3);
        Assert.Equal(100 - 24, box.Top, 3);
        Assert.Equal(100 + 340, box.Bottom, 3);
    }

    [Fact]
    public void BodyBox_IsClampedToTheImage()
    {
        var box = MaskMath.BodyBox(Face(0.0, 0.8, 0.05, 0.1), 1000, 500);

        Assert.Equal(0, box.Left);
        Assert.Equal(500, box.Bottom);
        Assert.True(box.Right <= 1000 && box.Top >= 0);
    }

    [Fact]
    public void FaceCentre_IsInPixels()
    {
        var (x, y) = MaskMath.FaceCentre(Face(0.2, 0.4, 0.1, 0.2), 1000, 500);

        Assert.Equal(250, x, 3);
        Assert.Equal(250, y, 3);
    }

    // ------------------------------------------------------------------ people (round 2)

    /// <summary>A YOLOX output with every row below threshold except the ones given (row index, raw values).</summary>
    private static float[] YoloxOutput(params (int Row, float Cx, float Cy, float LogW, float LogH, float Obj, float Person)[] rows)
    {
        var output = new float[8400 * 85];
        foreach (var r in rows)
        {
            var d = r.Row * 85;
            (output[d], output[d + 1], output[d + 2], output[d + 3], output[d + 4], output[d + 5]) = (r.Cx, r.Cy, r.LogW, r.LogH, r.Obj, r.Person);
        }

        return output;
    }

    [Fact]
    public void DecodePeople_UsesTheGridCellAndStride_AndUndoesTheLetterbox()
    {
        // Stride 8 has 80 × 80 cells: row 81 is cell (1, 1). Centre (1.5, 1.5) × 8 = (12, 12); size e^0 × 8 = 8.
        // The image was scaled by 0.5 into the square, so in the image: centre (24, 24), size 16.
        var people = MaskMath.DecodePeople(YoloxOutput((81, 0.5f, 0.5f, 0f, 0f, 0.9f, 0.9f)), 0.5, 1000, 1000, 0.5f);

        var (box, score) = Assert.Single(people);
        Assert.Equal(new PixelBox(16, 16, 32, 32), box);
        Assert.Equal(0.81f, score, 3);
    }

    [Fact]
    public void DecodePeople_StrideRowsFollowEachOther()
    {
        // The first stride-16 row comes after the 6,400 stride-8 rows: cell (0, 0) at stride 16.
        var (box, _) = Assert.Single(MaskMath.DecodePeople(YoloxOutput((6400, 0.5f, 0.5f, 0f, 0f, 1f, 1f)), 1, 640, 640, 0.5f));

        Assert.Equal(new PixelBox(0, 0, 16, 16), box);
    }

    [Fact]
    public void DecodePeople_ScoreIsObjectnessTimesPerson_AndDropsLowScores()
    {
        // 0.9 × 0.5 = 0.45 is below 0.5.
        Assert.Empty(MaskMath.DecodePeople(YoloxOutput((81, 0.5f, 0.5f, 0f, 0f, 0.9f, 0.5f)), 1, 640, 640, 0.5f));
    }

    [Fact]
    public void DecodePeople_MergesOverlappingBoxes_KeepingTheBest()
    {
        // Two cells next to each other with large boxes overlap almost completely; the higher score wins.
        var people = MaskMath.DecodePeople(YoloxOutput((81, 0.5f, 0.5f, 3f, 3f, 0.8f, 0.8f), (82, 0.4f, 0.5f, 3f, 3f, 0.9f, 0.9f)), 1, 640, 640, 0.5f);

        Assert.Equal(0.81f, Assert.Single(people).Score, 3);
    }

    [Fact]
    public void DecodePeople_RejectsTheWrongOutputSize() =>
        Assert.Throws<ArgumentException>(() => MaskMath.DecodePeople(new float[85 * 10], 1, 640, 640, 0.5f));

    [Fact]
    public void MainPeople_KeepsThoseAtLeastHalfTheTallest_LeftToRight()
    {
        var main = MaskMath.MainPeople(
        [
            new PixelBox(500, 0, 600, 400),   // tallest, 400
            new PixelBox(100, 100, 150, 300), // 200: exactly half, kept
            new PixelBox(800, 0, 820, 150),   // 150: a distant fielder, dropped
        ]);

        Assert.Equal([100f, 500f], main.Select(b => b.Left));
    }

    [Fact]
    public void MainPeople_EmptyStaysEmpty() => Assert.Empty(MaskMath.MainPeople([]));

    [Theory]
    [InlineData(0, 0, 10, 10, 0, 0, 10, 10, 1.0)]
    [InlineData(0, 0, 10, 10, 5, 0, 15, 10, 1.0 / 3)]
    [InlineData(0, 0, 10, 10, 20, 20, 30, 30, 0.0)]
    public void Iou_IsIntersectionOverUnion(float al, float at, float ar, float ab, float bl, float bt, float br, float bb, double expected) =>
        Assert.Equal(expected, MaskMath.Iou(new PixelBox(al, at, ar, ab), new PixelBox(bl, bt, br, bb)), 6);

    [Fact]
    public void YoloxScale_FitsTheLongSideInto640() => Assert.Equal(640 / 1024.0, MaskMath.YoloxScale(1024, 683), 9);

    [Fact]
    public void ScorePeople_CountsGrades_SplitByOneOrSeveralPeople()
    {
        PersonMaskItem P(string id, int main) => new(id, id + ".jpg", main + 1, main, 50, 700, 30, 0.05);
        var sheet = new JudgingSheets.SheetFile("mask-people", "people-test", DateTimeOffset.Now, "x", 0, null, null, null, [P("s001", 1), P("s002", 3), P("s003", 1)]);
        var report = new StringBuilder();

        MaskSheet.ScorePeople(sheet, new Dictionary<string, string[]> { ["s001"] = ["good"], ["s002"] = ["bad"], ["s003"] = ["usable"] }, report);
        var text = report.ToString();

        Assert.Contains("good 1 · usable 1 · bad 1; good or usable **67 %**", text, StringComparison.Ordinal);
        Assert.Contains("one main person: good or usable on 2 of 2", text, StringComparison.Ordinal);
        Assert.Contains("several main people: good or usable on 0 of 1", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ input sizes

    [Theory]
    [InlineData(6000, 4000, 1024, 683)] // 682.67 rounds up, as SAM's ResizeLongestSide does
    [InlineData(4000, 6000, 683, 1024)]
    [InlineData(1024, 1024, 1024, 1024)]
    public void LongSideSize_RoundsLikeSam(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), MaskMath.LongSideSize(w, h, 1024));

    [Theory]
    [InlineData(1024, 683, 736, 512)]  // short side to 512, long side 767 floored to ×32
    [InlineData(683, 1024, 512, 736)]
    [InlineData(400, 300, 672, 512)]   // smaller than 512: scaled up
    [InlineData(600, 500, 576, 480)]   // between: kept, floored to ×32
    public void ModnetSize_FollowsTheModnetDemo(int w, int h, int ew, int eh) =>
        Assert.Equal((ew, eh), MaskMath.ModnetSize(w, h));

    // ------------------------------------------------------------------ mask post-processing

    [Fact]
    public void SamMask_CropsThePaddingAndScalesToTheImage()
    {
        // A 1024 × 512 image fills the top half of SAM's padded square. Logits: positive in the left half of that area.
        var low = new float[256 * 256];
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                low[(y * 256) + x] = y < 128 && x < 128 ? 5f : -5f;
            }
        }

        var mask = MaskMath.SamMask(low, 1024, 512, 200, 100);

        Assert.Equal(1f, mask[(50 * 200) + 50]);   // left, inside
        Assert.Equal(0f, mask[(50 * 200) + 150]);  // right, outside
        Assert.Equal(1f, mask[(99 * 200) + 10]);   // bottom row of the image is still the top half of the square
        Assert.Equal(0.5, MaskMath.Coverage(mask), 2);
    }

    [Fact]
    public void SamMask_RejectsTheWrongLogitSize() =>
        Assert.Throws<ArgumentException>(() => MaskMath.SamMask(new float[100], 1024, 683, 10, 10));

    [Fact]
    public void Resize_KeepsAConstantMapConstant()
    {
        var resized = MaskMath.Resize(Enumerable.Repeat(0.25f, 12).ToArray(), 4, 3, 9, 7);

        Assert.All(resized, v => Assert.Equal(0.25f, v, 5));
    }

    [Fact]
    public void Union_TakesTheLargerAlpha()
    {
        var union = MaskMath.Union([[0f, 0.6f, 1f], [0.3f, 0.2f, 0f]]);

        Assert.Equal([0.3f, 0.6f, 1f], union);
    }

    [Fact]
    public void Overlay_KeepsTheSubject_DarkensTheRest_AndOutlinesTheEdge()
    {
        // 4 × 1 white image, mask covers the left two pixels.
        var photo = new AnalysisImage(4, 1, Enumerable.Repeat((byte)200, 16).ToArray());

        var view = MaskMath.Overlay(photo, [1f, 1f, 0f, 0f]).Bgra;

        Assert.Equal(200, view[0]);                         // inside, unchanged
        Assert.Equal((40, 190, 255), (view[4], view[5], view[6])); // edge pixel, amber
        Assert.Equal(60, view[12]);                          // outside: grey × 0.3
    }

    // ------------------------------------------------------------------ grading

    private static MaskItem Item(string id, bool samOnLeft) => new(id, id + ".jpg", samOnLeft, 1, 300, 20, 80, 0.1, 0.2);

    [Fact]
    public void Pair_UnblindsTheSides()
    {
        var rows = MaskSheet.Pair(
            [Item("m001", samOnLeft: true), Item("m002", samOnLeft: false)],
            new Dictionary<string, string[]> { ["m001"] = ["good", "bad"], ["m002"] = ["good", "bad"] });

        Assert.Equal((MaskGrade.Good, MaskGrade.Bad), (rows[0].Sam, rows[0].Modnet));
        Assert.Equal((MaskGrade.Bad, MaskGrade.Good), (rows[1].Sam, rows[1].Modnet));
    }

    [Fact]
    public void Pair_RejectsAMissingGradeOrUnknownWord()
    {
        Assert.Throws<FormatException>(() => MaskSheet.Pair([Item("m001", true)], new Dictionary<string, string[]> { ["m001"] = ["good"] }));
        Assert.Throws<FormatException>(() => MaskSheet.Pair([Item("m001", true)], new Dictionary<string, string[]> { ["m001"] = ["good", "great"] }));
    }

    [Fact]
    public void Score_ReportsGradesPerModel_AndWhichWasBetter()
    {
        var sheet = new JudgingSheets.SheetFile("mask", "mask-test", DateTimeOffset.Now, "x", 0.6, null, null,
            [Item("m001", true), Item("m002", false), Item("m003", true)]);
        var answers = new Dictionary<string, string[]>
        {
            ["m001"] = ["good", "bad"],     // SAM good, MODNet bad
            ["m002"] = ["usable", "good"],  // SAM good, MODNet usable
            ["m003"] = ["bad", "bad"],      // both bad
        };
        var report = new StringBuilder();

        MaskSheet.Score(sheet, answers, report);
        var text = report.ToString();

        Assert.Contains("**MobileSAM:** good 2 · usable 0 · bad 1", text, StringComparison.Ordinal);
        Assert.Contains("**MODNet:** good 0 · usable 1 · bad 2", text, StringComparison.Ordinal);
        Assert.Contains("Graded better: MobileSAM on 2, MODNet on 0, equal on 1", text, StringComparison.Ordinal);
    }
}
