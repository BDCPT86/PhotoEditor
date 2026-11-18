using System.Text;
using FrameForge.Evaluation.Sheets;

namespace FrameForge.Evaluation.Tests;

/// <summary>Round 3 (B3): padded against unpadded subject masks, graded blind.</summary>
public sealed class PaddingSheetTests
{
    private static PaddingItem Item(string id, bool paddedOnLeft, bool identical = false) =>
        new(id, id + ".jpg", paddedOnLeft, "2 people", 4000, 3900, 0.06, 0.05, identical);

    [Fact]
    public void Score_UnblindsEachSide_AndCountsWhichWasBetter()
    {
        var sheet = new JudgingSheets.SheetFile("mask-padding", "padding-test", DateTimeOffset.Now, "x", 0, null, null, Padding: [Item("p001", true), Item("p002", false), Item("p003", true, identical: true)]);
        var answers = new Dictionary<string, string[]>
        {
            ["p001"] = ["good", "usable"],  // padded on the left: padded better
            ["p002"] = ["bad", "usable"],   // padded on the right: padded better
            ["p003"] = ["usable", "usable"],
        };
        var report = new StringBuilder();

        PaddingSheet.Score(sheet, answers, report);

        var text = report.ToString();
        Assert.Contains("**Padded:** good 1 · usable 2 · bad 0", text, StringComparison.Ordinal);
        Assert.Contains("**Unpadded (round 2):** good 0 · usable 2 · bad 1", text, StringComparison.Ordinal);
        Assert.Contains("Graded better: padded on 2, unpadded on 0, equal on 1", text, StringComparison.Ordinal);
        Assert.Contains("Identical masks (graded on looks alone): 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Score_NeedsBothGrades()
    {
        var sheet = new JudgingSheets.SheetFile("mask-padding", "padding-test", DateTimeOffset.Now, "x", 0, null, null, Padding: [Item("p001", true)]);

        Assert.Throws<FormatException>(() => PaddingSheet.Score(sheet, new Dictionary<string, string[]> { ["p001"] = ["good"] }, new StringBuilder()));
    }
}
