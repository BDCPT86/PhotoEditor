using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Export;

/// <summary>M09 A4: which files of a RAW + JPEG pair are exported, how they are counted, and how they are named.</summary>
public sealed class ExportFramesTests
{
    private static readonly DateTime Taken = new(2026, 11, 14, 9, 30, 15);

    private static Photo Make(string fileName) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = $@"C:\Shoot\{fileName}",
        FileName = fileName,
        FolderPath = @"C:\Shoot",
        CaptureLocalTime = Taken,
        Status = ImportStatus.Imported,
    };

    /// <summary>IMG_1 on its own, then the pair IMG_2.JPG + IMG_2.CR3, then IMG_3.CR3 on its own.</summary>
    private static List<ExportItem> Items()
    {
        var jpeg = Make("IMG_2.JPG");
        return
        [
            new ExportItem(Make("IMG_1.JPG"), PhotoDecision.Unset),
            new ExportItem(jpeg, new PhotoDecision(CullState.Keep)) { Cover = jpeg },
            new ExportItem(Make("IMG_2.CR3"), new PhotoDecision(CullState.Keep)) { Cover = jpeg },
            new ExportItem(Make("IMG_3.CR3"), PhotoDecision.Unset),
        ];
    }

    private static List<string> Names(IEnumerable<ExportItem> items) => items.Select(i => i.Photo.FileName).ToList();

    [Fact]
    public void TheChoice_AppliesToPairs_AndPhotosOnTheirOwnAreAlwaysIncluded()
    {
        var items = Items();

        Assert.Equal(["IMG_1.JPG", "IMG_2.JPG", "IMG_2.CR3", "IMG_3.CR3"], Names(ExportFrames.Select(items, PairFiles.Both)));
        Assert.Equal(["IMG_1.JPG", "IMG_2.CR3", "IMG_3.CR3"], Names(ExportFrames.Select(items, PairFiles.RawOnly)));
        Assert.Equal(["IMG_1.JPG", "IMG_2.JPG", "IMG_3.CR3"], Names(ExportFrames.Select(items, PairFiles.JpegOnly)));
    }

    [Fact]
    public void EditedJpegs_AreMadeOncePerPair_FromTheCover()
    {
        Assert.Equal(["IMG_1.JPG", "IMG_2.JPG", "IMG_3.CR3"], Names(ExportFrames.Covers(Items())));
    }

    [Fact]
    public void APair_CountsAsOnePhoto()
    {
        Assert.Equal(3, ExportFrames.Count(Items()));
        Assert.True(ExportFrames.HasPairs(Items()));
        Assert.False(ExportFrames.HasPairs(Items().Where(i => i.Cover is null)));
    }

    [Fact]
    public void APattern_NamesBothFilesFromTheCover_WithOneSequenceNumber()
    {
        Assert.True(ExportNameTemplate.TryParse("{date:yyyy-MM-dd}_{seq:3}", out var naming, out _));

        var names = ExportFrames.WantedNames(Items(), naming).Select(n => n.Name);

        Assert.Equal(["2026-11-14_001.JPG", "2026-11-14_002.JPG", "2026-11-14_002.CR3", "2026-11-14_003.CR3"], names);
    }

    [Fact]
    public void WithNoPattern_EachFileKeepsItsOwnName()
    {
        var jpeg = Make("IMG_7.JPG");
        List<ExportItem> items = [new(jpeg, PhotoDecision.Unset) { Cover = jpeg }, new(Make("img_7.cr3"), PhotoDecision.Unset) { Cover = jpeg }];

        Assert.Equal(["IMG_7.JPG", "img_7.cr3"], ExportFrames.WantedNames(items, ExportNameTemplate.Original).Select(n => n.Name));
    }

    [Fact]
    public void AClash_RenamesBothFilesOfAPairAlike()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var plan = ExportNamePlanner.PlanFrames([("IMG_2.JPG", a), ("IMG_2.CR3", a), ("IMG_3.JPG", b)], ["IMG_2.JPG"]);

        Assert.Equal(["IMG_2 (2).JPG", "IMG_2 (2).CR3", "IMG_3.JPG"], plan.Select(p => p.TargetFileName));
        Assert.True(plan[1].WasRenamed);
        Assert.False(plan[2].WasRenamed);
    }

    [Fact]
    public void AClash_WithOnlyTheRawName_StillMovesThePairTogether()
    {
        var a = Guid.NewGuid();

        var plan = ExportNamePlanner.PlanFrames([("IMG_2.JPG", a), ("IMG_2.CR3", a)], ["img_2.cr3", "IMG_2 (2).JPG"]);

        Assert.Equal(["IMG_2 (3).JPG", "IMG_2 (3).CR3"], plan.Select(p => p.TargetFileName));
    }

    [Fact]
    public void TheSameNameTwiceInAFrame_IsPlannedOnItsOwn_AndDoesNotHang()
    {
        var a = Guid.NewGuid();

        var plan = ExportNamePlanner.PlanFrames([("IMG_2.jpg", a), ("IMG_2.jpg", a)], []);

        Assert.Equal(["IMG_2.jpg", "IMG_2 (2).jpg"], plan.Select(p => p.TargetFileName));
    }

    [Fact]
    public void AProfile_KeepsThePairChoice_AndOlderProfilesMeanBoth()
    {
        var profile = new ExportProfile { Name = "RAW only", PairFiles = PairFiles.RawOnly };

        Assert.Empty(profile.Validate());
        Assert.False(profile.HasSameSettings(ExportProfiles.Originals));
        Assert.Equal(PairFiles.Both, ExportProfiles.Originals.PairFiles);
        Assert.NotEmpty((profile with { PairFiles = (PairFiles)9 }).Validate());
    }
}
