using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;

namespace FrameForge.App.Tests;

/// <summary>
/// M09 A2: a RAW + JPEG pair is one tile, and a decision on it reaches both files as one undo step. A3: analysis and
/// edits work on the cover (the JPEG) only.
/// </summary>
public sealed class PairWorkflowTests
{
    private static readonly int[] Both = [0, 2];

    [Fact]
    public async Task APair_IsOneTile_ShowingTheJpeg_AndCountsAsOnePhoto()
    {
        using var f = new CullingFixture(photoCount: 4, pairedRaws: [1, 2]);
        await f.LoadAsync();

        Assert.Equal(4, f.Library.Photos.Count);
        Assert.DoesNotContain(f.Library.Photos, t => t.FileName.EndsWith(".CR3", StringComparison.Ordinal));
        Assert.Equal("4 photos", f.Library.CountText.Split("  ·  ")[0]);

        var paired = f.Tile(1);
        Assert.True(paired.IsPaired);
        Assert.Same(f.Raws[1], paired.Partner!.Photo);
        Assert.StartsWith("RAW + JPEG", paired.DetailsText, StringComparison.Ordinal);
        Assert.Contains("RAW + JPEG", paired.AccessibleStatus, StringComparison.Ordinal);
        Assert.Contains("IMG_01.CR3", paired.PairToolTip, StringComparison.Ordinal);
        Assert.False(f.Tile(0).IsPaired);
        Assert.Null(f.Tile(0).PairToolTip);
    }

    [Fact]
    public async Task ARawFile_FromAnotherTime_IsNotPaired_AndKeepsItsOwnTile()
    {
        using var f = new CullingFixture(photoCount: 2, pairedRaws: [1]);
        f.Raws[1].CaptureLocalTime = f.Raws[1].CaptureLocalTime!.Value.AddDays(2);
        await f.LoadAsync();

        Assert.Equal(3, f.Library.Photos.Count);
        Assert.False(f.Tile(1).IsPaired);
    }

    [Fact]
    public async Task ADecision_ReachesBothFiles_AndIsReportedAsOnePhoto()
    {
        using var f = new CullingFixture(photoCount: 3, pairedRaws: [1]);
        await f.LoadAsync();
        f.Select(1);

        f.Library.TryHandle(CullingAction.Keep);
        f.Library.TryHandle(CullingAction.Rate4);

        var expected = new PhotoDecision(CullState.Keep, 4);
        Assert.Equal(expected, f.Tile(1).Decision);
        Assert.Equal(expected, f.Tile(1).Partner!.Decision);
        Assert.Equal(expected, await StoredAsync(f, f.Raws[1].Id, d => d == expected));
        Assert.Equal(expected, await f.StoredAsync(1, d => d == expected));
        await WaitUntil(() => f.Library.StatusMessage is not null);
        Assert.DoesNotContain("photos", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneUndo_RevertsBothFiles()
    {
        using var f = new CullingFixture(photoCount: 3, pairedRaws: [0, 2]);
        await f.LoadAsync();
        f.Select(0, 2);

        f.Library.TryHandle(CullingAction.Reject);
        await WaitUntil(() => f.Library.StatusMessage is not null);
        Assert.Contains("2 photos", f.Library.StatusMessage, StringComparison.Ordinal);
        Assert.All(Both, i => Assert.Equal(CullState.Reject, f.Tile(i).Partner!.Decision.State));

        f.Library.TryHandle(CullingAction.Undo);
        await WaitUntil(() => Both.All(i => f.Tile(i).Partner!.Decision.State == CullState.Unset));

        Assert.All(Both, i => Assert.Equal(CullState.Unset, f.Tile(i).Decision.State));
        Assert.Equal(PhotoDecision.Unset, await StoredAsync(f, f.Raws[2].Id, d => d == PhotoDecision.Unset));
        Assert.False(f.Controller.CanUndo);
    }

    [Fact]
    public async Task FilesThatDisagree_ShowTheJpegsDecision_AndTheNextChangeWritesItToBoth()
    {
        using var f = new CullingFixture(photoCount: 2, pairedRaws: [1]);
        await f.Decisions.SaveAsync([(f.Photos[1].Id, new PhotoDecision(CullState.Keep, 3)), (f.Raws[1].Id, new PhotoDecision(CullState.Reject))], TestContext.Current.CancellationToken);
        await f.LoadAsync();

        Assert.Equal(new PhotoDecision(CullState.Keep, 3), f.Tile(1).Decision);

        // Keep is already the JPEG's state: nothing changes on the tile, but the RAW file is brought into step.
        f.Select(1);
        f.Library.TryHandle(CullingAction.Keep);

        Assert.Equal(new PhotoDecision(CullState.Keep, 3), f.Tile(1).Partner!.Decision);
        Assert.Equal(new PhotoDecision(CullState.Keep, 3), await StoredAsync(f, f.Raws[1].Id, d => d.State == CullState.Keep));

        // Undo gives the RAW file its own earlier decision back.
        f.Library.TryHandle(CullingAction.Undo);
        await WaitUntil(() => f.Tile(1).Partner!.Decision.State == CullState.Reject);
        Assert.Equal(new PhotoDecision(CullState.Keep, 3), f.Tile(1).Decision);
    }

    [Fact]
    public async Task Filters_CountAPairOnce()
    {
        using var f = new CullingFixture(photoCount: 3, pairedRaws: [0, 1]);
        await f.LoadAsync();
        f.Select(0, 1);
        f.Library.TryHandle(CullingAction.Keep);

        f.Library.Filter.State = StateFilterChoice.Keep;

        Assert.Equal(2, f.Library.Photos.Count);
    }

    // ------------------------------------------------------------------ A4: export

    [Theory]
    [InlineData(PairFiles.Both, new[] { "IMG_00.jpg", "IMG_01.jpg", "IMG_01.CR3" })]
    [InlineData(PairFiles.RawOnly, new[] { "IMG_00.jpg", "IMG_01.CR3" })]
    [InlineData(PairFiles.JpegOnly, new[] { "IMG_00.jpg", "IMG_01.jpg" })]
    public async Task CopyFiles_CopiesTheChosenFilesOfAPair(PairFiles choice, string[] expected)
    {
        using var f = await OpenExportAsync();
        Assert.True(f.Export.HasPairs);

        f.Export.PairFiles = choice;
        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);

        Assert.Equal(expected, Assert.Single(f.Exporter.Calls).Select(i => i.Photo.FileName));
    }

    [Fact]
    public async Task ThePanel_CountsPhotos_AndFilesWhenAPairGivesTwo()
    {
        using var f = await OpenExportAsync();

        Assert.Equal("2 photo(s)  ·  3 files", f.Export.CountText);
        Assert.Equal("IMG_00.jpg", f.Export.NamePreview);

        f.Export.PairFiles = PairFiles.JpegOnly;
        Assert.Equal("2 photo(s)", f.Export.CountText);

        f.Export.Mode = ExportMode.Manifest; // the manifest lists every file
        Assert.Equal("2 photo(s)  ·  3 files", f.Export.CountText);
    }

    [Fact]
    public async Task ThePreview_ShowsBothNamesOfAPair()
    {
        using var f = new CullingFixture(photoCount: 1, pairedRaws: [0]);
        await f.LoadAsync();
        f.Library.OpenExportCommand.Execute(null);
        f.Export.Scope = ExportScope.CurrentView;

        f.Export.NameTemplateText = "{seq:3}-shoot";

        Assert.Equal("001-shoot.jpg, 001-shoot.CR3", f.Export.NamePreview);
    }

    [Fact]
    public async Task EditedJpegs_AreMadeOncePerPair_FromTheJpeg()
    {
        using var f = await OpenExportAsync();

        f.Export.Mode = ExportMode.EditedJpegs;
        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);

        var (items, _) = Assert.Single(f.EditedExporter.Calls);
        Assert.Equal(["IMG_00.jpg", "IMG_01.jpg"], items.Select(i => i.Photo.FileName));
        Assert.Equal("2 photo(s)", f.Export.CountText);
    }

    [Fact]
    public async Task TheResult_CountsFilesAndPhotos()
    {
        using var f = await OpenExportAsync();

        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);

        Assert.StartsWith("Copied 3 files (2 photos) to", f.Export.ResultText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoPairs_TheChoiceIsHidden()
    {
        using var f = new CullingFixture(photoCount: 2);
        await f.LoadAsync();
        f.Library.OpenExportCommand.Execute(null);
        f.Export.Scope = ExportScope.CurrentView;

        Assert.False(f.Export.HasPairs);
        Assert.Equal("2 photo(s)", f.Export.CountText);
    }

    private static async Task<CullingFixture> OpenExportAsync()
    {
        var f = new CullingFixture(photoCount: 2, pairedRaws: [1]);
        await f.LoadAsync();
        f.Library.OpenExportCommand.Execute(null);
        f.Export.Scope = ExportScope.CurrentView;
        return f;
    }

    // ------------------------------------------------------------------ A3: the cover only

    [Fact]
    public async Task Analyze_IsGivenTheCovers_NotTheirRawFiles()
    {
        using var f = new CullingFixture(photoCount: 3, pairedRaws: [0, 1]);
        await f.LoadAsync();

        await f.AnalysisPanel.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(["IMG_00.jpg", "IMG_01.jpg", "IMG_02.jpg"], f.AnalysisService.LastAnalyzed.Select(p => p.FileName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(f.AiService.LastAnalyzed, p => f.Raws.ContainsValue(p));
    }

    [Fact]
    public async Task APair_IsNotGroupedAsSimilarToItself()
    {
        // The RAW file's preview is the camera's JPEG, so their hashes match. Before pairs, that made every pair a "similar" group.
        using var f = new CullingFixture(photoCount: 2, pairedRaws: [0]);
        f.AnalysisService.Set(f.Photos[0], 0b0000);
        f.AnalysisService.Set(f.Raws[0], 0b0000);
        f.AnalysisService.Set(f.Photos[1], ulong.MaxValue);
        await f.LoadAsync();

        Assert.True(f.Tile(0).Insight.IsAnalysed);
        Assert.Null(f.Tile(0).GroupBadgeText);
        Assert.False(f.Tile(0).ShowExactBadge);
    }

    [Fact]
    public async Task APreset_IsAppliedToTheCover_AndNotToItsRawFile()
    {
        using var f = new CullingFixture(photoCount: 2, pairedRaws: [1]);
        f.Edit.Presets.Stored.Add(new EditPreset(Guid.NewGuid(), "Warm", EditRecipe.Neutral with { Temperature = 40 }, PresetGroups.WhiteBalance));
        await f.LoadAsync();
        f.Select(1);

        f.Library.OpenPresetsCommand.Execute(null);
        Assert.Equal(1, f.PresetPanel.TargetCount);
        f.PresetPanel.SelectedPreset = f.PresetPanel.Presets.Single();
        await f.PresetPanel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(40, f.Edit.Edits.Stored[f.Photos[1].Id].Temperature);
        Assert.False(f.Edit.Edits.Stored.ContainsKey(f.Raws[1].Id));
        Assert.True(f.Tile(1).Partner!.Recipe.IsNeutral);
    }

    [Fact]
    public async Task AnEarlierEditOfTheRawFile_StaysStored_ButTheTileShowsTheCoversEdit()
    {
        using var f = new CullingFixture(photoCount: 1, pairedRaws: [0]);
        f.Edit.Edits.Stored[f.Raws[0].Id] = EditRecipe.Neutral with { Exposure = 1 };
        await f.LoadAsync();

        Assert.False(f.Tile(0).IsEdited);
        Assert.True(f.Edit.Edits.Stored.ContainsKey(f.Raws[0].Id));
    }

    private static async Task<PhotoDecision> StoredAsync(CullingFixture f, Guid id, Func<PhotoDecision, bool> until)
    {
        for (var i = 0; i < 100 && !until(f.Decisions.Get(id)); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return f.Decisions.Get(id);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
