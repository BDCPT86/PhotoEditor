using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;

namespace FrameForge.App.Tests;

/// <summary>Editing through the real Library/Viewer/Develop view models with in-memory storage.</summary>
public sealed class EditWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StoredEdits_AreAppliedOnLoad_AndShowAsEdited()
    {
        using var f = new CullingFixture();
        f.Edit.Edits.Stored[f.Photos[2].Id] = EditRecipe.Neutral with { Exposure = 1 };

        await f.LoadAsync();

        Assert.True(f.Tile(2).IsEdited);
        Assert.False(f.Tile(1).IsEdited);
        Assert.Contains("edited", f.Tile(2).AccessibleStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D_OpensViewerAndDevelop_EditsSaveAndShowInViewer()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(1);

        Assert.True(f.Library.TryHandle(CullingAction.ToggleDevelop));
        await WaitForAsync(() => f.Viewer.Develop.IsOpen);
        Assert.True(f.Library.IsViewerOpen);

        f.Viewer.Develop.Exposure = 0.5;
        await f.Viewer.Develop.FlushAsync();
        Assert.Equal(0.5, f.Tile(1).Recipe.Exposure);
        await WaitForAsync(() => f.Viewer.Develop.DisplayImage is not null); // the preview source loads asynchronously
        await f.Viewer.Develop.RenderLoop;
        Assert.Same(f.Viewer.Develop.DisplayImage, f.Viewer.DisplayImage);

        // Ctrl+Z while developing undoes the edit, not a decision.
        Assert.True(f.Library.TryHandle(CullingAction.Undo));
        await WaitForAsync(() => f.Tile(1).Recipe.IsNeutral);

        // Closing Develop renders the full-size edit through the edit renderer.
        f.Viewer.Develop.Exposure = 0.7;
        f.Library.TryHandle(CullingAction.ToggleDevelop);
        await WaitForAsync(() => !f.Viewer.Develop.IsOpen);
        Assert.Equal(0.7, f.Tile(1).Recipe.Exposure);

        // The full-size reload starts just after Develop closes; wait for it rather than a load that may be older.
        await WaitForAsync(() =>
        {
            lock (f.Edit.Renderer.Rendered)
            {
                return f.Edit.Renderer.Rendered.Any(r => r.Exposure == 0.7);
            }
        });
    }

    [Fact]
    public async Task ClosingTheViewer_SavesAPendingEdit()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.ToggleDevelop);
        await WaitForAsync(() => f.Viewer.Develop.IsOpen);
        f.Viewer.Develop.CommitDelay = TimeSpan.FromSeconds(30);

        f.Viewer.Develop.Vibrance = 33;
        await f.Library.CloseViewerCommand.ExecuteAsync(null);

        Assert.False(f.Library.IsViewerOpen);
        Assert.Equal(33, f.Edit.Edits.Stored[f.Photos[0].Id].Vibrance);
    }

    [Fact]
    public async Task DecisionKeys_StillWorkWhileDeveloping_AndDoNotTouchEdits()
    {
        using var f = new CullingFixture(autoAdvance: false);
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.ToggleDevelop);
        await WaitForAsync(() => f.Viewer.Develop.IsOpen);

        f.Library.TryHandle(CullingAction.Keep);

        Assert.Equal(CullState.Keep, (await f.StoredAsync(0, d => d.State == CullState.Keep)).State);
        Assert.True(f.Tile(0).Recipe.IsNeutral);
    }

    [Fact]
    public async Task PresetBatch_AppliesToSelection_AndCanBeUndone()
    {
        using var f = new CullingFixture();
        f.Edit.Presets.Stored.Add(new EditPreset(Guid.NewGuid(), "Warm", EditRecipe.Neutral with { Temperature = 40 }, PresetGroups.WhiteBalance));
        f.Edit.Edits.Stored[f.Photos[1].Id] = EditRecipe.Neutral with { Exposure = -1 };
        await f.LoadAsync();
        f.Select(0, 1, 2);

        f.Library.OpenPresetsCommand.Execute(null);
        Assert.Equal(3, f.PresetPanel.TargetCount);
        f.PresetPanel.SelectedPreset = f.PresetPanel.Presets.Single();
        await f.PresetPanel.ApplyCommand.ExecuteAsync(null);

        foreach (var i in Enumerable.Range(0, 3))
        {
            Assert.Equal(40, f.Tile(i).Recipe.Temperature);
        }

        Assert.Equal(-1, f.Tile(1).Recipe.Exposure); // untouched group kept
        Assert.True(f.Tile(3).Recipe.IsNeutral);
        Assert.Contains("3 photo", f.PresetPanel.ResultText, StringComparison.Ordinal);

        await f.PresetPanel.UndoBatchCommand.ExecuteAsync(null);

        Assert.True(f.Tile(0).Recipe.IsNeutral);
        Assert.Equal(EditRecipe.Neutral with { Exposure = -1 }, f.Tile(1).Recipe);
        Assert.False(f.Edit.Edits.Stored.ContainsKey(f.Photos[0].Id));
    }

    [Fact]
    public async Task EditedExport_SendsEachPhotosRecipe_AndOptions()
    {
        using var f = new CullingFixture();
        f.Edit.Edits.Stored[f.Photos[0].Id] = EditRecipe.Neutral with { Saturation = 20 };
        await f.LoadAsync();
        f.Library.TryHandle(CullingAction.Keep); // nothing selected: no-op
        f.Select(0, 1);
        f.Library.TryHandle(CullingAction.Keep);
        await f.StoredAsync(1, d => d.State == CullState.Keep);

        f.Library.OpenExportCommand.Execute(null);
        f.Export.Mode = ExportMode.EditedJpegs;
        f.Export.Quality = 85;
        f.Export.MaxLongSideText = "2000";
        f.Export.Metadata = ExportMetadata.AllButLocation;
        f.Export.WriteRatingAndLabel = false;
        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);

        var (items, options) = Assert.Single(f.EditedExporter.Calls);
        Assert.Equal(2, items.Count);
        Assert.Equal(20, items.Single(i => i.Photo.Id == f.Photos[0].Id).Recipe.Saturation);
        Assert.True(items.Single(i => i.Photo.Id == f.Photos[1].Id).Recipe.IsNeutral);
        Assert.Equal((85, 2000), (options.Quality, options.MaxLongSide!.Value));
        Assert.Equal((ExportMetadata.AllButLocation, false), (options.Metadata, options.WriteRatingAndLabel));
        Assert.Contains("edited JPEG", f.Export.ResultText, StringComparison.Ordinal);
        Assert.Empty(f.Exporter.Calls); // the plain copier was not used
    }

    [Fact]
    public async Task EditedExport_RejectsABadLongSide()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.Keep);
        await f.StoredAsync(0, d => d.State == CullState.Keep);
        f.Library.OpenExportCommand.Execute(null);
        f.Export.Mode = ExportMode.EditedJpegs;
        f.Export.MaxLongSideText = "big";
        f.FolderPicker.Next = @"D:\Out";

        await f.Export.StartCommand.ExecuteAsync(null);

        Assert.Empty(f.EditedExporter.Calls);
        Assert.True(f.Export.ResultIsWarning);
    }

    [Fact]
    public void EditedTile_RequestsAFreshThumbnail_ButKeepsShowingTheOldOne()
    {
        var thumbnails = new FakeThumbnailSource();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), thumbnails);
        var bitmap = TestPhotos.Bitmap();
        thumbnails.Cached[tile.Id] = bitmap;

        Assert.Same(bitmap, tile.Thumbnail);
        Assert.Empty(thumbnails.Requests); // current for the neutral recipe

        tile.Recipe = EditRecipe.Neutral with { Exposure = 1 };

        Assert.Same(bitmap, tile.Thumbnail);            // stale, still shown
        Assert.Same(tile, Assert.Single(thumbnails.Requests)); // and reloaded for the new recipe
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.True(condition(), "condition not reached");
    }
}
