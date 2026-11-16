using System.Windows.Input;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;

namespace FrameForge.App.Tests;

public sealed class CullingWorkflowTests
{
    // ------------------------------------------------------------------ keyboard map

    [Theory]
    [InlineData(Key.P, ModifierKeys.None, CullingAction.Keep)]
    [InlineData(Key.X, ModifierKeys.None, CullingAction.Reject)]
    [InlineData(Key.V, ModifierKeys.None, CullingAction.Review)]
    [InlineData(Key.U, ModifierKeys.None, CullingAction.Unset)]
    [InlineData(Key.D3, ModifierKeys.None, CullingAction.Rate3)]
    [InlineData(Key.NumPad5, ModifierKeys.None, CullingAction.Rate5)]
    [InlineData(Key.D0, ModifierKeys.None, CullingAction.ClearRating)]
    [InlineData(Key.D6, ModifierKeys.None, CullingAction.LabelRed)]
    [InlineData(Key.D9, ModifierKeys.None, CullingAction.LabelBlue)]
    [InlineData(Key.D0, ModifierKeys.Shift, CullingAction.ClearLabel)]
    [InlineData(Key.Z, ModifierKeys.Control, CullingAction.Undo)]
    [InlineData(Key.Y, ModifierKeys.Control, CullingAction.Redo)]
    [InlineData(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, CullingAction.Redo)]
    [InlineData(Key.C, ModifierKeys.None, CullingAction.Compare)]
    [InlineData(Key.F1, ModifierKeys.None, CullingAction.ToggleHelp)]
    public void KeyMap(Key key, ModifierKeys modifiers, CullingAction expected) =>
        Assert.Equal(expected, CullingShortcuts.Resolve(key, modifiers));

    [Theory]
    [InlineData(Key.P, ModifierKeys.Control)]   // Ctrl+P is not Keep
    [InlineData(Key.P, ModifierKeys.Alt)]
    [InlineData(Key.Z, ModifierKeys.None)]      // Z belongs to the viewer zoom
    [InlineData(Key.Left, ModifierKeys.None)]   // navigation stays with the list/viewer
    [InlineData(Key.Enter, ModifierKeys.None)]
    public void UnmappedKeys_AreLeftAlone(Key key, ModifierKeys modifiers) =>
        Assert.Null(CullingShortcuts.Resolve(key, modifiers));

    [Fact]
    public void EveryDecisionAction_HasAnEdit()
    {
        foreach (var action in Enum.GetValues<CullingAction>().Where(a => a is >= CullingAction.Keep and <= CullingAction.ClearLabel))
        {
            Assert.NotNull(CullingShortcuts.ToEdit(action));
        }
    }

    [Fact]
    public void HelpMentionsEveryDecisionKey()
    {
        var keys = CullingShortcuts.Help.SelectMany(g => g.Items).Select(i => i.Keys).ToList();
        foreach (var expected in new[] { "P", "X", "V", "U", "1 – 5", "0", "Shift+0", "Ctrl+Z / Ctrl+Y", "C", "F1 or ?" })
        {
            Assert.Contains(expected, keys);
        }
    }

    // ------------------------------------------------------------------ grid

    [Fact]
    public async Task Grid_DecisionAppliesToEverySelectedPhoto_AndPersists()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(1, 3);

        Assert.True(f.Library.TryHandle(CullingAction.Keep));

        Assert.Equal(CullState.Keep, f.Tile(1).Decision.State);
        Assert.Equal(CullState.Keep, f.Tile(3).Decision.State);
        Assert.Equal(CullState.Unset, f.Tile(2).Decision.State);
        Assert.Equal(CullState.Keep, (await f.StoredAsync(3, d => d.State == CullState.Keep)).State);
    }

    [Fact]
    public async Task Grid_RatingAndLabel_AreIndependentOfState()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);

        f.Library.TryHandle(CullingAction.Reject);
        f.Library.TryHandle(CullingAction.Rate4);
        f.Library.TryHandle(CullingAction.LabelGreen);

        Assert.Equal(new PhotoDecision(CullState.Reject, 4, ColourLabel.Green), f.Tile(0).Decision);

        f.Library.TryHandle(CullingAction.ClearRating);
        f.Library.TryHandle(CullingAction.ClearLabel);
        Assert.Equal(new PhotoDecision(CullState.Reject), f.Tile(0).Decision);
    }

    [Fact]
    public async Task Grid_UnderUnsetFilter_DecidedPhotosLeave_AndSelectionMovesOn()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Library.Filter.State = StateFilterChoice.Unset;
        f.Select(2);
        var next = f.Tile(3);

        f.Library.TryHandle(CullingAction.Keep);

        Assert.Equal(5, f.Library.Photos.Count);
        Assert.DoesNotContain(f.Library.Photos, t => t.FileName == "IMG_02.jpg");
        Assert.Same(next, f.Library.SelectedPhoto);
    }

    [Fact]
    public async Task UndoAndRedo_RestoreDecisions_AndFilteredView()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Library.Filter.State = StateFilterChoice.Unset;
        f.Select(0, 1);
        var tiles = new[] { f.Tile(0), f.Tile(1) };

        f.Library.TryHandle(CullingAction.Reject);
        Assert.Equal(4, f.Library.Photos.Count);

        f.Library.TryHandle(CullingAction.Undo);
        await WaitUntil(() => tiles.All(t => t.Decision.State == CullState.Unset));
        Assert.Equal(6, f.Library.Photos.Count); // undone photos match the filter again and reappear

        f.Library.TryHandle(CullingAction.Redo);
        await WaitUntil(() => tiles.All(t => t.Decision.State == CullState.Reject));
        Assert.Equal(4, f.Library.Photos.Count);
    }

    [Fact]
    public async Task FailedSave_RevertsTheDecision_AndShowsAWarning()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);
        f.Decisions.FailSaves = true;

        f.Library.TryHandle(CullingAction.Keep);
        Assert.Equal(CullState.Keep, f.Tile(0).Decision.State); // optimistic

        await WaitUntil(() => f.Tile(0).Decision.State == CullState.Unset);
        Assert.True(f.Library.StatusIsWarning);
        Assert.False(f.Controller.CanUndo); // the failed change is not left in history
    }

    [Fact]
    public async Task Filters_AreDeterministic_AcrossRebuilds()
    {
        using var f = new CullingFixture(photoCount: 30);
        await f.LoadAsync();
        f.Select(Enumerable.Range(0, 30).Where(i => i % 3 == 0).ToArray());
        f.Library.TryHandle(CullingAction.Rate3);
        f.Library.Filter.Sort = Core.Library.LibrarySort.Rating;

        var first = f.Library.Photos.Select(t => t.FileName).ToList();
        f.Library.TryHandle(CullingAction.Refresh);
        var second = f.Library.Photos.Select(t => t.FileName).ToList();

        Assert.Equal(first, second);
        Assert.All(first.Take(10), name => Assert.Equal(0, int.Parse(name[4..6], System.Globalization.CultureInfo.InvariantCulture) % 3));
    }

    // ------------------------------------------------------------------ viewer

    [Fact]
    public async Task Viewer_StateKeyAppliesToCurrentPhoto_AndAdvances()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Library.OpenViewerCommand.Execute(f.Tile(0));
        f.Select(4, 5); // grid selection must not matter while the viewer is open

        f.Library.TryHandle(CullingAction.Keep);

        Assert.Equal(CullState.Keep, f.Tile(0).Decision.State);
        Assert.Equal(CullState.Unset, f.Tile(4).Decision.State);
        Assert.Equal("IMG_01.jpg", f.Viewer.Current!.FileName);
    }

    [Fact]
    public async Task Viewer_RatingDoesNotAdvance()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Library.OpenViewerCommand.Execute(f.Tile(2));

        f.Library.TryHandle(CullingAction.Rate5);

        Assert.Equal(5, f.Tile(2).Decision.Rating);
        Assert.Equal("IMG_02.jpg", f.Viewer.Current!.FileName);
    }

    [Fact]
    public async Task Viewer_AutoAdvanceCanBeTurnedOff()
    {
        using var f = new CullingFixture(autoAdvance: false);
        await f.LoadAsync();
        f.Library.OpenViewerCommand.Execute(f.Tile(0));

        f.Library.TryHandle(CullingAction.Reject);

        Assert.Equal("IMG_00.jpg", f.Viewer.Current!.FileName);
    }

    [Fact]
    public async Task Viewer_UsesAStableSnapshot_UnderAFilter_AndGridUpdatesOnClose()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Library.Filter.State = StateFilterChoice.Unset;
        f.Library.OpenViewerCommand.Execute(f.Tile(0));

        f.Library.TryHandle(CullingAction.Keep); // advances to IMG_01
        f.Library.TryHandle(CullingAction.Keep); // advances to IMG_02
        Assert.Equal("IMG_02.jpg", f.Viewer.Current!.FileName);
        Assert.Equal(6, f.Library.Photos.Count); // grid not pruned while viewing

        f.Viewer.RequestCloseCommand.Execute(null);

        Assert.Equal(4, f.Library.Photos.Count);
        Assert.Equal("IMG_02.jpg", f.Library.SelectedPhoto!.FileName);
    }

    // ------------------------------------------------------------------ compare

    [Fact]
    public async Task Compare_NeedsTwoPhotos()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(1);

        f.Library.TryHandle(CullingAction.Compare);

        Assert.False(f.Library.IsCompareOpen);
        Assert.Contains("Select 2", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_DecisionsApplyToTheActivePanelOnly()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(4, 1, 2); // click order differs from grid order

        f.Library.TryHandle(CullingAction.Compare);
        await f.Compare.CurrentLoad;

        Assert.True(f.Library.IsCompareOpen);
        Assert.Equal(["IMG_01.jpg", "IMG_02.jpg", "IMG_04.jpg"], f.Compare.Panels.Select(p => p.Tile.FileName)); // grid order
        Assert.All(f.Compare.Panels, p => Assert.NotNull(p.Image));
        Assert.All(f.Loader.DisplaySizesRequested, size => Assert.Equal(CompareViewModel.DisplayPixelSize, size)); // not full resolution

        f.Library.TryHandle(CullingAction.Reject);
        f.Compare.NextCommand.Execute(null);
        f.Library.TryHandle(CullingAction.Keep);

        Assert.Equal(CullState.Reject, f.Tile(1).Decision.State);
        Assert.Equal(CullState.Keep, f.Tile(2).Decision.State);
        Assert.Equal(CullState.Unset, f.Tile(4).Decision.State);
        Assert.Equal("IMG_02.jpg", f.Compare.Active!.Tile.FileName); // no auto-advance in compare
    }

    [Fact]
    public async Task Compare_TakesAtMostFourPhotos_AndCloseReleasesImages()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0, 1, 2, 3, 4, 5);

        f.Library.TryHandle(CullingAction.Compare);
        await f.Compare.CurrentLoad;
        var panels = f.Compare.Panels;
        Assert.Equal(CompareViewModel.MaxPhotos, panels.Count);

        f.Compare.RequestCloseCommand.Execute(null);

        Assert.False(f.Library.IsCompareOpen);
        Assert.All(panels, p => Assert.Null(p.Image));
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public async Task Export_KeepScope_IgnoresTheCurrentFilter()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(1, 4);
        f.Library.TryHandle(CullingAction.Keep);
        f.Library.Filter.State = StateFilterChoice.Reject; // grid shows nothing

        f.Library.OpenExportCommand.Execute(null);

        Assert.Equal(2, f.Export.KeepCount);
        Assert.Equal(0, f.Export.ViewCount);
    }

    [Fact]
    public void ExportSummaryText_ExplainsRenamesAndFailures()
    {
        var text = ExportViewModel.Describe(
            new ExportSummary
            {
                Destination = @"D:\Out",
                Requested = 5,
                Exported = 4,
                Renamed = 2,
                Failures = [new ExportFailure(@"C:\A\gone.jpg", "not found")],
            },
            manifest: false);

        Assert.Contains("Copied 4", text, StringComparison.Ordinal);
        Assert.Contains("2 renamed", text, StringComparison.Ordinal);
        Assert.Contains("gone.jpg", text, StringComparison.Ordinal);
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
