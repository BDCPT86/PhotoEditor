using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Decisions;

namespace FrameForge.App.Tests;

/// <summary>M09 A2: a RAW + JPEG pair is one tile, and a decision on it reaches both files as one undo step.</summary>
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

    [Fact]
    public async Task Export_StillCopiesBothFilesOfAPair()
    {
        using var f = new CullingFixture(photoCount: 2, pairedRaws: [1]);
        await f.LoadAsync();
        f.Library.OpenExportCommand.Execute(null);
        f.Export.Scope = ExportScope.CurrentView;

        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);

        var items = Assert.Single(f.Exporter.Calls);
        Assert.Equal(["IMG_00.jpg", "IMG_01.jpg", "IMG_01.CR3"], items.Select(i => i.Photo.FileName));
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
