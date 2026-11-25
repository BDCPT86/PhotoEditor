using System.IO;
using System.Text;
using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Auto;
using FrameForge.Core.Editing;
using FrameForge.Evaluation.Sheets;
using FrameForge.Imaging;

namespace FrameForge.Evaluation.Tests;

public sealed class JudgingSheetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ff-sheet-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ------------------------------------------------------------------ answers

    [Fact]
    public void ParseAnswers_SkipsHeaderAndBlanks_LaterLinesWin()
    {
        var answers = SheetScoring.ParseAnswers(["id,answer", "", "p001,left", "c002,0.1,0.2,0.5,0.5", "p001,same"]);

        Assert.Equal(["same"], answers["p001"]);
        Assert.Equal(["0.1", "0.2", "0.5", "0.5"], answers["c002"]);
    }

    [Fact]
    public void ParseAnswers_RejectsALineWithoutAnAnswer_WithLineNumber()
    {
        var ex = Assert.Throws<FormatException>(() => SheetScoring.ParseAnswers(["id,answer", "p001"]));
        Assert.StartsWith("Line 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseCrop_ReadsNoneAndRectangles_AndRejectsOutOfFrame()
    {
        Assert.Null(SheetScoring.ParseCrop(["none"]));
        Assert.Equal(new UserCrop(0.1, 0.2, 0.5, 0.6), SheetScoring.ParseCrop(["0.1", "0.2", "0.5", "0.6"]));
        Assert.Throws<FormatException>(() => SheetScoring.ParseCrop(["0.6", "0", "0.5", "0.5"])); // runs off the right edge
        Assert.Throws<FormatException>(() => SheetScoring.ParseCrop(["0.1", "0.2"]));
    }

    // ------------------------------------------------------------------ light statistics

    [Fact]
    public void TallyLight_MapsLeftRightToAutoOrOriginal_UsingTheHiddenKey()
    {
        LightPair[] pairs =
        [
            new("p001", "a.jpg", AutoOnLeft: true, "Natural", "exposure +0.30"),
            new("p002", "b.jpg", AutoOnLeft: false, "Natural", "exposure +0.30"),
            new("p003", "c.jpg", AutoOnLeft: true, "Natural", "exposure +0.30"),
            new("p004", "d.jpg", AutoOnLeft: true, "Natural", "exposure +0.30"),
        ];
        var answers = new Dictionary<string, LightChoice> { ["p001"] = LightChoice.Left, ["p002"] = LightChoice.Left, ["p003"] = LightChoice.Same };

        var tally = SheetScoring.TallyLight(pairs, answers);

        Assert.Equal(1, tally.Auto); // p001: left was Auto
        Assert.Equal(1, tally.Original); // p002: left was the original
        Assert.Equal(1, tally.Same);
        Assert.Equal(1, tally.Unanswered);
        Assert.Equal(0.5, tally.AutoShare);
    }

    [Fact]
    public void Wilson_MatchesKnownValues_AndIsWideForSmallSamples()
    {
        var (low, high) = SheetScoring.Wilson(8, 10);
        Assert.Equal(0.490, low, 3);
        Assert.Equal(0.943, high, 3);
        Assert.Equal((0.0, 1.0), SheetScoring.Wilson(0, 0));
    }

    [Theory]
    [InlineData(8, 2, 0.109375)]
    [InlineData(5, 5, 1.0)]
    [InlineData(10, 0, 0.001953125)]
    [InlineData(0, 0, 1.0)]
    public void SignTest_IsTheExactTwoSidedBinomialTest(int a, int b, double expected)
    {
        Assert.Equal(expected, SheetScoring.SignTestP(a, b), 9);
    }

    // ------------------------------------------------------------------ crop agreement

    [Fact]
    public void Iou_IsOneForIdentical_ZeroForDisjoint_AndAQuarterForHalfOverlap()
    {
        var a = new CropRect(0, 0, 0.5, 0.5);
        Assert.Equal(1, SheetScoring.Iou(a, a), 9);
        Assert.Equal(0, SheetScoring.Iou(a, new CropRect(0.5, 0.5, 0.5, 0.5)), 9);
        Assert.Equal(1.0 / 3, SheetScoring.Iou(a, new CropRect(0.25, 0, 0.5, 0.5)), 9); // overlap 1/8, union 3/8
    }

    private static CropItem Player() =>
        new("c001", "x.jpg", 6000, 4000, [new FaceFinding(0.6f, 0.3f, 0.027f, 0.04f, 0.9f, 120)]); // a typical player, so Tight, Medium and Wide all differ

    [Fact]
    public void CompareCrop_UserCropEqualToAutoMedium_ScoresOne_AtTheUsersRatio()
    {
        var item = Player();
        var medium = AutoCrop.Suggest(item.SourceWidth, item.SourceHeight, item.Faces, EditRecipe.Neutral, 4.0 / 5, 0.6).Named("Medium")!.Recipe.Crop;

        var result = SheetScoring.CompareCrop(item, new UserCrop(medium.X, medium.Y, medium.Width, medium.Height), 0.6);

        Assert.Equal("Medium", result.BestOption);
        Assert.Equal(1, result.BestIou!.Value, 6);
        Assert.False(result.UserKeptFrame);
    }

    [Fact]
    public void CompareCrop_FlagsKeepTheFrame_AndCropsBelowTheMinimumSize()
    {
        var item = Player();

        var kept = SheetScoring.CompareCrop(item, null, 0.6);
        var tiny = SheetScoring.CompareCrop(item, new UserCrop(0.58, 0.22, 0.12, 0.18), 0.6); // 720 × 720 px

        Assert.True(kept.UserKeptFrame);
        Assert.True(tiny.UserBelowMinimum);
        Assert.False(kept.UserBelowMinimum);
    }

    [Fact]
    public void CompareCrop_ReportsWhenAutoHasNothingToOffer()
    {
        var noFaces = new CropItem("c009", "y.jpg", 6000, 4000, []);
        var result = SheetScoring.CompareCrop(noFaces, new UserCrop(0, 0, 0.5, 0.5), 0.6);
        Assert.Null(result.BestIou);
        Assert.NotNull(result.Unavailable);
    }

    // ------------------------------------------------------------------ sheets end to end

    [Fact]
    public void Sample_IsDeterministicPerSeed_AndCoversEveryFile()
    {
        string[] files = ["c.jpg", "a.jpg", "e.jpg", "b.jpg", "d.jpg"];
        Assert.Equal(JudgingSheets.Sample(files, 3), JudgingSheets.Sample(files.Reverse(), 3));
        Assert.Equal(files.Order(), JudgingSheets.Sample(files, 3).Order());
    }

    [Fact]
    public void CheckFolders_RefusesASheetInsideThePhotos_OrANonEmptyFolder()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var used = Directory.CreateDirectory(Path.Combine(_root, "used")).FullName;
        File.WriteAllText(Path.Combine(used, "x.txt"), "x");

        Assert.Contains("inside the photos", JudgingSheets.CheckFolders(photos, Path.Combine(photos, "sheet")), StringComparison.Ordinal);
        Assert.Contains("new or empty", JudgingSheets.CheckFolders(photos, used), StringComparison.Ordinal);
        Assert.Null(JudgingSheets.CheckFolders(photos, Path.Combine(_root, "fresh")));
    }

    [Fact]
    public void LightSheet_PageIsBlind_AndAnswersScoreAgainstTheKey()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        for (var i = 0; i < 4; i++)
        {
            // Dark, slightly cool photos: Auto light always has something to change.
            var shade = 50 + (i * 8);
            WriteJpeg(Path.Combine(photos, $"shot{i}.jpg"), AnalysisImage.Create(320, 240, (x, y) => ((byte)(shade + (x % 9)), (byte)(shade + (y % 7)), (byte)(shade + 12))));
        }

        var sheetDir = Path.Combine(_root, "sheet");
        Directory.CreateDirectory(sheetDir);
        var report = new StringBuilder();
        JudgingSheets.BuildLight(photos, sheetDir, count: 3, seed: 7, report);

        var page = File.ReadAllText(Path.Combine(sheetDir, "index.html"));
        var sheet = JudgingSheets.ReadSheet(sheetDir);
        Assert.Equal(3, sheet.Pairs!.Count);
        Assert.Equal(6, Directory.GetFiles(Path.Combine(sheetDir, "img")).Length);
        Assert.DoesNotContain("autoOnLeft", page, StringComparison.OrdinalIgnoreCase); // the key stays in sheet.json
        Assert.All(Enumerable.Range(0, 4), i => Assert.DoesNotContain($"shot{i}.jpg", page, StringComparison.Ordinal)); // no original file names either
        Assert.All(sheet.Pairs, p => Assert.Contains("exposure", p.Change, StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(photos, "shot0.jpg"))); // originals untouched in place

        // Always pick the Auto side: the report must say Auto won every decided pair.
        var answers = Path.Combine(_root, "answers.csv");
        File.WriteAllLines(answers, ["id,answer", .. sheet.Pairs.Select(p => p.Id + "," + (p.AutoOnLeft ? "left" : "right"))]);
        var scored = new StringBuilder();
        Assert.Equal(0, JudgingSheets.Score(sheetDir, answers, scored));
        Assert.Contains("Preferred Auto light: **3** · preferred as shot: **0**", scored.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PageFill_EscapesScriptEndings()
    {
        Assert.Contains("<\\/script>", SheetPages.Fill("x __DATA__ y", "{\"a\":\"</script>\"}"), StringComparison.Ordinal);
    }

    private static void WriteJpeg(string path, AnalysisImage image)
    {
        using var stream = File.Create(path);
        new WicJpegEncoder().Write(image, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 92);
    }
}
