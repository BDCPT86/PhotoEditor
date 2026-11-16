using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Export;

public sealed class ExportPlanningTests
{
    [Fact]
    public void Planner_KeepsNamesWhenFree()
    {
        var plan = ExportNamePlanner.Plan([@"C:\A\one.jpg", @"C:\A\two.jpg"], []);

        Assert.Equal(["one.jpg", "two.jpg"], plan.Select(p => p.TargetFileName));
        Assert.All(plan, p => Assert.False(p.WasRenamed));
    }

    [Fact]
    public void Planner_AvoidsExistingFiles_CaseInsensitively()
    {
        var plan = ExportNamePlanner.Plan([@"C:\A\IMG_0001.JPG"], ["img_0001.jpg", "IMG_0001 (2).JPG"]);

        var file = Assert.Single(plan);
        Assert.Equal("IMG_0001 (3).JPG", file.TargetFileName);
        Assert.True(file.WasRenamed);
    }

    [Fact]
    public void Planner_AvoidsCollisionsWithinTheBatch()
    {
        var plan = ExportNamePlanner.Plan([@"C:\Day1\IMG_1.jpg", @"C:\Day2\IMG_1.jpg", @"C:\Day3\img_1.JPG"], []);

        Assert.Equal(["IMG_1.jpg", "IMG_1 (2).jpg", "img_1 (3).JPG"], plan.Select(p => p.TargetFileName));
    }

    [Fact]
    public void Planner_HandlesNamesWithoutExtensionAndWithExistingSuffix()
    {
        Assert.Equal("README (2)", ExportNamePlanner.NextAvailable("README", new HashSet<string>(["readme"], StringComparer.OrdinalIgnoreCase)));
        Assert.Equal("a (2) (2).jpg", ExportNamePlanner.NextAvailable("a (2).jpg", new HashSet<string>(["a (2).jpg"], StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Planner_IsDeterministic()
    {
        string[] sources = [@"C:\X\a.jpg", @"C:\Y\a.jpg", @"C:\Z\b.jpg"];
        Assert.Equal(
            ExportNamePlanner.Plan(sources, ["a.jpg"]).Select(p => p.TargetFileName),
            ExportNamePlanner.Plan(sources, ["a.jpg"]).Select(p => p.TargetFileName));
    }

    private static ExportItem Item(string path, PhotoDecision? decision = null, string? model = null) => new(
        new Photo
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            FolderPath = Path.GetDirectoryName(path)!,
            CameraModel = model,
            CaptureLocalTime = new DateTime(2026, 5, 1, 9, 30, 15, DateTimeKind.Unspecified),
            Width = 6000,
            Height = 4000,
        },
        decision ?? new PhotoDecision(CullState.Keep, 4, ColourLabel.Green));

    private static string[] WriteManifest(params ExportItem[] items)
    {
        using var writer = new StringWriter();
        ManifestWriter.Write(writer, items);
        return writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Manifest_HasHeaderAndOneRowPerPhoto()
    {
        var lines = WriteManifest(Item(@"C:\A\one.jpg", model: "Model X"), Item(@"C:\A\two.jpg", PhotoDecision.Unset));

        Assert.Equal("FilePath,FileName,Decision,Rating,Label,CaptureTime,Width,Height,Camera", lines[0]);
        Assert.Equal(@"C:\A\one.jpg,one.jpg,Keep,4,Green,2026-05-01 09:30:15,6000,4000,Model X", lines[1]);
        Assert.Equal(@"C:\A\two.jpg,two.jpg,Unset,,,2026-05-01 09:30:15,6000,4000,", lines[2]);
    }

    [Fact]
    public void Manifest_QuotesCommasQuotesAndNewlines()
    {
        var lines = WriteManifest(Item(@"C:\A\a, b.jpg", model: "Cam \"Pro\""));

        Assert.StartsWith("\"C:\\A\\a, b.jpg\",\"a, b.jpg\"", lines[1], StringComparison.Ordinal);
        Assert.EndsWith("\"Cam \"\"Pro\"\"\"", lines[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\").jpg")]
    [InlineData("+1.jpg")]
    [InlineData("-1.jpg")]
    [InlineData("@cmd.jpg")]
    public void Manifest_NeutralisesSpreadsheetFormulas(string fileName)
    {
        var line = WriteManifest(Item($@"C:\A\{fileName}"))[1];
        var fileNameCell = line.Split(',')[1].Trim('"');

        Assert.StartsWith("'", fileNameCell, StringComparison.Ordinal);
    }
}
