using FrameForge.App.Controls;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Editing;

namespace FrameForge.App.Tests;

public sealed class DevelopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (EditStack Stack, PhotoTileViewModel Tile) Setup()
    {
        var stack = new EditStack();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), new FakeThumbnailSource());
        return (stack, tile);
    }

    [Fact]
    public async Task Open_LoadsSliders_AndRendersAPreview()
    {
        var (stack, tile) = Setup();
        tile.Recipe = EditRecipe.Neutral with { Exposure = 1.5, Contrast = 20 };

        await stack.Develop.OpenAsync(tile);
        await stack.Develop.RenderLoop;

        Assert.True(stack.Develop.IsOpen);
        Assert.Equal(1.5, stack.Develop.Exposure);
        Assert.Equal(20, stack.Develop.Contrast);
        Assert.NotNull(stack.Develop.DisplayImage);
        Assert.Equal(1, stack.Renderer.SourceLoads); // decoded once, re-rendered from memory
    }

    [Fact]
    public async Task SliderChange_IsSaved_ShownOnTile_AndUndoable()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);

        stack.Develop.Exposure = 0.8;
        await stack.Develop.FlushAsync();

        Assert.Equal(0.8, tile.Recipe.Exposure);
        Assert.Equal(0.8, stack.Edits.Stored[tile.Id].Exposure);
        Assert.True(tile.IsEdited);
        Assert.True(stack.Develop.CanUndo);

        await stack.Develop.UndoCommand.ExecuteAsync(null);

        Assert.Equal(0, stack.Develop.Exposure);
        Assert.True(tile.Recipe.IsNeutral);
        Assert.False(stack.Edits.Stored.ContainsKey(tile.Id)); // neutral = no stored row
        Assert.True(stack.Develop.CanRedo);

        await stack.Develop.RedoCommand.ExecuteAsync(null);
        Assert.Equal(0.8, tile.Recipe.Exposure);
    }

    [Fact]
    public async Task ManyQuickChanges_BecomeOneUndoStep()
    {
        var (stack, tile) = Setup();
        stack.Develop.CommitDelay = TimeSpan.FromSeconds(30); // nothing commits until flushed
        await stack.Develop.OpenAsync(tile);

        for (var i = 1; i <= 20; i++)
        {
            stack.Develop.Contrast = i; // like dragging a slider
        }

        await stack.Develop.FlushAsync();
        await stack.Develop.UndoCommand.ExecuteAsync(null);

        Assert.Equal(0, stack.Develop.Contrast);
        Assert.False(stack.Develop.CanUndo);
    }

    [Fact]
    public async Task FailedSave_RevertsTheTileAndSliders_AndWarns()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Edits.FailSaves = true;

        stack.Develop.Saturation = 40;
        await stack.Develop.FlushAsync();

        Assert.True(tile.Recipe.IsNeutral);
        Assert.Equal(0, stack.Develop.Saturation);
        Assert.True(stack.Develop.MessageIsWarning);
        Assert.False(stack.Develop.CanUndo); // a failed save is not an undo step
    }

    [Fact]
    public async Task SwitchingPhotos_SavesThePendingChangeToThePreviousPhoto()
    {
        var (stack, first) = Setup();
        var second = new PhotoTileViewModel(TestPhotos.Create("b.jpg"), new FakeThumbnailSource());
        stack.Develop.CommitDelay = TimeSpan.FromSeconds(30);
        await stack.Develop.OpenAsync(first);

        stack.Develop.Shadows = 35;
        await stack.Develop.OpenAsync(second);

        Assert.Equal(35, first.Recipe.Shadows);
        Assert.True(second.Recipe.IsNeutral);
        Assert.Equal(0, stack.Develop.Shadows);
    }

    [Fact]
    public async Task ResetSetting_AndResetAll()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Develop.Exposure = 1;
        stack.Develop.Tint = 30;

        stack.Develop.ResetSettingCommand.Execute(nameof(DevelopViewModel.Exposure));
        Assert.Equal(0, stack.Develop.Exposure);
        Assert.Equal(30, stack.Develop.Tint);

        await stack.Develop.ResetAllCommand.ExecuteAsync(null);
        Assert.True(stack.Develop.CurrentRecipe.IsNeutral);
        Assert.True(tile.Recipe.IsNeutral);
    }

    [Fact]
    public async Task CropMode_PreviewsTheWholeFrame_ThenAppliesTheCrop()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        await stack.Develop.RenderLoop;
        var full = stack.Develop.DisplayImage!;

        stack.Develop.IsCropMode = true;
        stack.Develop.Crop = new CropRect(0.25, 0.25, 0.5, 0.5);
        await stack.Develop.RenderLoop;
        Assert.Equal(full.PixelWidth, stack.Develop.DisplayImage!.PixelWidth); // still the whole frame

        stack.Develop.IsCropMode = false;
        await stack.Develop.RenderLoop;
        await stack.Develop.FlushAsync();
        Assert.Equal(full.PixelWidth / 2, stack.Develop.DisplayImage!.PixelWidth);
        Assert.Equal(new CropRect(0.25, 0.25, 0.5, 0.5), tile.Recipe.Crop);
    }

    [Fact]
    public async Task AspectChoice_FitsTheLargestCentredCrop()
    {
        var (stack, tile) = Setup(); // source is 96×64 (3:2)
        await stack.Develop.OpenAsync(tile);

        stack.Develop.CropAspect = CropAspect.Square;

        var crop = stack.Develop.Crop;
        Assert.Equal(1, crop.Height, 6);
        Assert.Equal(64.0 / 96, crop.Width, 6);
        Assert.Equal((1 - crop.Width) / 2, crop.X, 6);
        Assert.Equal(2.0 / 3, stack.Develop.LockedCropRatio, 6); // normalised width÷height: square pixels in a 3:2 frame
    }

    [Fact]
    public async Task QuarterTurn_ResetsACrop()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Develop.Crop = new CropRect(0, 0, 0.5, 0.5);

        stack.Develop.RotateRightCommand.Execute(null);

        Assert.Equal(1, stack.Develop.QuarterTurns);
        Assert.True(stack.Develop.Crop.IsFull);
    }

    [Fact]
    public async Task Presets_SaveApplyAndDelete()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Develop.Temperature = 25;
        stack.Develop.Exposure = 1;
        stack.Develop.NewPresetName = "Warm";
        stack.Develop.PresetIncludesExposure = false;

        await stack.Develop.SavePresetCommand.ExecuteAsync(null);
        var preset = Assert.Single(stack.Presets.Stored);
        Assert.Equal(PresetGroups.WhiteBalance | PresetGroups.Tone | PresetGroups.Colour, preset.Groups);

        await stack.Develop.ResetAllCommand.ExecuteAsync(null);
        stack.Develop.SelectedPreset = stack.Develop.Presets.Single();
        await stack.Develop.ApplyPresetCommand.ExecuteAsync(null);
        Assert.Equal(25, tile.Recipe.Temperature);
        Assert.Equal(0, tile.Recipe.Exposure); // exposure was not included

        await stack.Develop.DeletePresetCommand.ExecuteAsync(null);
        Assert.Empty(stack.Presets.Stored);
    }

    [Fact]
    public async Task DuplicatePresetName_IsReported()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Develop.NewPresetName = "Look";
        await stack.Develop.SavePresetCommand.ExecuteAsync(null);
        stack.Develop.NewPresetName = "look";
        await stack.Develop.SavePresetCommand.ExecuteAsync(null);

        Assert.Single(stack.Presets.Stored);
        Assert.True(stack.Develop.MessageIsWarning);
    }

    [Fact]
    public void CropMath_Fit_KeepsInsideAndCentres()
    {
        var crop = CropMath.Fit(16.0 / 9, 1.5, new CropRect(0.6, 0.6, 0.4, 0.4));
        Assert.Equal(1, crop.Width, 6);
        Assert.Equal(1.5 / (16.0 / 9), crop.Height, 6);
        Assert.InRange(crop.Y + crop.Height, 0, 1 + 1e-9);
    }

    [Theory]
    [InlineData((int)CropOverlay.DragMode.Move, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5)]           // moving is clamped inside the frame
    [InlineData((int)(CropOverlay.DragMode.Right | CropOverlay.DragMode.Bottom), 0.1, 0.1, 0, 0, 0.6, 0.6)]
    [InlineData((int)CropOverlay.DragMode.Left, -0.5, 0, 0, 0, 1, 0.5)]                   // cannot go past the left edge
    public void CropOverlay_Drag(int modeValue, double dx, double dy, double x, double y, double w, double h)
    {
        var mode = (CropOverlay.DragMode)modeValue;
        var start = new CropRect(0, 0, 0.5, 0.5);
        if (mode == CropOverlay.DragMode.Left)
        {
            start = new CropRect(0.5, 0, 0.5, 0.5);
        }

        var result = CropOverlay.Apply(start, mode, dx, dy, lockedRatio: 0);
        Assert.Equal(new CropRect(x, y, w, h), result);
    }

    [Fact]
    public void CropOverlay_LockedRatio_IsKeptWhileResizing()
    {
        var result = CropOverlay.Apply(new CropRect(0, 0, 0.4, 0.4), CropOverlay.DragMode.Right | CropOverlay.DragMode.Bottom, 0.3, 0.05, lockedRatio: 1);
        Assert.Equal(result.Width, result.Height, 6);
        Assert.True(result.X + result.Width <= 1 && result.Y + result.Height <= 1);
    }

    [Fact]
    public void Shortcuts_D_R_Backslash()
    {
        Assert.Equal(CullingAction.ToggleDevelop, CullingShortcuts.Resolve(System.Windows.Input.Key.D, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(CullingAction.ToggleCrop, CullingShortcuts.Resolve(System.Windows.Input.Key.R, System.Windows.Input.ModifierKeys.None));
        Assert.Equal(CullingAction.BeforeAfter, CullingShortcuts.Resolve(System.Windows.Input.Key.Oem5, System.Windows.Input.ModifierKeys.None));
    }
}
