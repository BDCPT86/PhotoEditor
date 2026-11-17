using System.Windows.Input;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Ai;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;

namespace FrameForge.App.Tests;

/// <summary>The Auto suggestions tray in Develop, Auto on many photos, and zoom to faces.</summary>
public sealed class AutoSuggestionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(EditStack Stack, PhotoTileViewModel Tile)> OpenAsync()
    {
        var stack = new EditStack();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), new FakeThumbnailSource());
        await stack.Develop.OpenAsync(tile);
        return (stack, tile);
    }

    private static async Task WaitForOptionsAsync(DevelopViewModel develop)
    {
        for (var i = 0; i < 300 && develop.IsComputingSuggestions; i++)
        {
            await Task.Delay(10, Ct);
        }

        Assert.False(develop.IsComputingSuggestions, "suggestions never finished");
    }

    [Fact]
    public async Task AutoLight_ShowsThreeOptions_FocusPreviews_NothingSavedYet()
    {
        var (stack, tile) = await OpenAsync();

        await stack.Develop.AutoLightCommand.ExecuteAsync(null);
        await WaitForOptionsAsync(stack.Develop);

        Assert.True(stack.Develop.IsSuggesting);
        Assert.Equal(["Natural", "Brighter", "Punchy"], stack.Develop.Suggestions.Select(s => s.Name));
        Assert.Same(stack.Develop.Suggestions[0], stack.Develop.FocusedSuggestion);
        Assert.True(stack.Develop.Suggestions[0].IsFocused);
        Assert.Same(stack.Develop.Suggestions[0].Preview, stack.Develop.DisplayImage); // the photo shows the focused option

        Assert.True(stack.Develop.HandleSuggestionKey(Key.Right));
        Assert.Same(stack.Develop.Suggestions[1], stack.Develop.FocusedSuggestion);
        Assert.False(stack.Develop.Suggestions[0].IsFocused);
        Assert.True(stack.Develop.HandleSuggestionKey(Key.D3));
        Assert.Equal("Punchy", stack.Develop.FocusedSuggestion!.Name);

        Assert.True(tile.Recipe.IsNeutral); // previewing never saves
        Assert.Empty(stack.Edits.Stored);
    }

    [Fact]
    public async Task Accept_AppliesOneUndoStep_AndClosesTheTray()
    {
        var (stack, tile) = await OpenAsync();
        await stack.Develop.AutoLightCommand.ExecuteAsync(null);
        await WaitForOptionsAsync(stack.Develop);
        var chosen = stack.Develop.Suggestions[1].Suggestion.Recipe;

        stack.Develop.HandleSuggestionKey(Key.Right);
        Assert.True(stack.Develop.HandleSuggestionKey(Key.Enter));
        await stack.Develop.FlushAsync();

        Assert.False(stack.Develop.IsSuggesting);
        Assert.Equal(chosen, tile.Recipe);
        Assert.Equal(chosen.Exposure, stack.Develop.Exposure);
        Assert.Contains("Brighter", stack.Develop.Message, StringComparison.Ordinal);

        await stack.Develop.UndoCommand.ExecuteAsync(null);
        Assert.True(tile.Recipe.IsNeutral);
        Assert.False(stack.Develop.CanUndo); // one step
    }

    [Fact]
    public async Task Dismiss_ChangesNothing()
    {
        var (stack, tile) = await OpenAsync();
        stack.Develop.Contrast = 15;
        await stack.Develop.FlushAsync();
        await stack.Develop.AutoLightCommand.ExecuteAsync(null);
        await WaitForOptionsAsync(stack.Develop);

        Assert.True(stack.Develop.HandleSuggestionKey(Key.Escape));

        Assert.False(stack.Develop.IsSuggesting);
        Assert.Equal(15, tile.Recipe.Contrast);
        Assert.Equal(15, stack.Develop.Contrast);
        Assert.NotSame(stack.Develop.DisplayImage, null);
    }

    [Fact]
    public async Task MovingASlider_ClosesStaleSuggestions()
    {
        var (stack, _) = await OpenAsync();
        await stack.Develop.AutoLightCommand.ExecuteAsync(null);
        await WaitForOptionsAsync(stack.Develop);

        stack.Develop.Saturation = 10;

        Assert.False(stack.Develop.IsSuggesting);
        Assert.Empty(stack.Develop.Suggestions);
    }

    [Fact]
    public async Task KeysAreNotTakenWhenTheTrayIsClosed()
    {
        var (stack, _) = await OpenAsync();
        Assert.False(stack.Develop.HandleSuggestionKey(Key.Right));
        Assert.False(stack.Develop.HandleSuggestionKey(Key.D1));
    }

    [Fact]
    public async Task AutoCrop_WithoutFaceData_ExplainsWhy()
    {
        var (stack, _) = await OpenAsync();

        await stack.Develop.AutoCropCommand.ExecuteAsync(null);

        Assert.True(stack.Develop.IsSuggesting);
        Assert.Empty(stack.Develop.Suggestions);
        Assert.Contains("not been analysed for faces", stack.Develop.SuggestionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoCrop_WithAFace_OffersCropsAtTheChosenRatio()
    {
        var stack = new EditStack();
        var photo = TestPhotos.Create("a.jpg");
        photo.Width = 6000;
        photo.Height = 4000;
        var tile = new PhotoTileViewModel(photo, new FakeThumbnailSource());
        stack.Faces.Analysed[tile.Id] = [new FaceFinding(0.6f, 0.25f, 0.08f, 0.12f, 0.95f, 150)];
        await stack.Develop.OpenAsync(tile);
        stack.Develop.CropAspect = CropAspect.Portrait4x5;
        Assert.Equal("Crop · 4:5", stack.Develop.AutoCropLabel);

        await stack.Develop.AutoCropCommand.ExecuteAsync(null);
        await WaitForOptionsAsync(stack.Develop);

        Assert.NotEmpty(stack.Develop.Suggestions);
        Assert.All(stack.Develop.Suggestions, s =>
        {
            var c = s.Suggestion.Recipe.Crop;
            Assert.Equal(0.8, c.Width * 6000 / (c.Height * 4000), 2);
        });
    }

    [Fact]
    public async Task AutoBatch_FromThePresetsPanel_IsUndoable_AndMarksPhotosForReview()
    {
        using var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0, 1, 2);
        f.Library.OpenPresetsCommand.Execute(null);
        f.PresetPanel.AutoLight = true;
        f.PresetPanel.LightVariant = "Brighter";
        f.PresetPanel.AutoCrop = true; // no face data in this fixture: noted, not fatal

        await f.PresetPanel.ApplyAutoCommand.ExecuteAsync(null);

        foreach (var i in new[] { 0, 1, 2 })
        {
            Assert.True(f.Tile(i).IsEdited);
            Assert.True(f.Tile(i).AutoApplied);
        }

        Assert.False(f.Tile(3).AutoApplied);
        Assert.Equal(3, f.PresetPanel.AutoNotes.Count);
        Assert.Contains("not cropped", f.PresetPanel.AutoNotes[0], StringComparison.Ordinal);

        f.Library.Filter.Analysis = AnalysisFilterChoice.AutoApplied;
        Assert.Equal(3, f.Library.Photos.Count);

        await f.PresetPanel.UndoBatchCommand.ExecuteAsync(null);
        Assert.Empty(f.Library.Photos); // the filtered view follows the undo

        f.Library.Filter.Analysis = AnalysisFilterChoice.Any;
        Assert.True(f.Tile(0).Recipe.IsNeutral);
        Assert.False(f.Tile(0).AutoApplied);
    }

    [Fact]
    public async Task ZoomToFaces_StepsLeftToRight_AndWraps()
    {
        using var f = new CullingFixture(autoAdvance: false);
        f.Photos[0].Width = 6000;
        f.Photos[0].Height = 4000;
        f.Edit.Faces.Analysed[f.Photos[0].Id] =
        [
            new FaceFinding(0.7f, 0.2f, 0.05f, 0.07f, 0.9f, 100),
            new FaceFinding(0.2f, 0.3f, 0.05f, 0.07f, 0.9f, 100),
            new FaceFinding(0.5f, 0.5f, 0.05f, 0.07f, 0.3f, 100), // below the score threshold: skipped
        ];
        await f.LoadAsync();
        f.Select(0);
        f.Library.OpenViewerCommand.Execute(null);

        Assert.True(f.Library.TryHandle(CullingAction.NextFace));
        Assert.Equal("Face 1 of 2", f.Viewer.FaceStatus);
        Assert.Equal(0.2, f.Viewer.FaceFocus!.X, 3);
        f.Library.TryHandle(CullingAction.NextFace);
        Assert.Equal(0.7, f.Viewer.FaceFocus!.X, 3);
        f.Library.TryHandle(CullingAction.NextFace);
        Assert.Equal("Face 1 of 2", f.Viewer.FaceStatus); // wraps
        f.Library.TryHandle(CullingAction.PreviousFace);
        Assert.Equal("Face 2 of 2", f.Viewer.FaceStatus);
        Assert.Equal(CullState.Unset, f.Tile(0).Decision.State);
    }

    [Fact]
    public async Task ZoomToFaces_FollowsTheCrop_AndExplainsMissingData()
    {
        using var f = new CullingFixture();
        f.Photos[0].Width = 6000;
        f.Photos[0].Height = 4000;
        f.Edit.Edits.Stored[f.Photos[0].Id] = EditRecipe.Neutral with { Crop = new CropRect(0.5, 0, 0.5, 1) };
        f.Edit.Faces.Analysed[f.Photos[0].Id] = [new FaceFinding(0.1f, 0.3f, 0.05f, 0.07f, 0.9f, 100), new FaceFinding(0.75f, 0.3f, 0.05f, 0.07f, 0.9f, 100)];
        await f.LoadAsync();
        f.Select(0);
        f.Library.OpenViewerCommand.Execute(null);

        f.Library.TryHandle(CullingAction.NextFace);
        Assert.Equal("Face 1 of 1", f.Viewer.FaceStatus); // the left face is cropped away
        Assert.Equal(0.5, f.Viewer.FaceFocus!.X, 3);           // 0.75 in the photo = 0.5 in the cropped half

        f.Viewer.NextCommand.Execute(null);
        f.Library.TryHandle(CullingAction.NextFace);
        Assert.Contains("No face data", f.Viewer.FaceStatus, StringComparison.Ordinal);
    }
}
