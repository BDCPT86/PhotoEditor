using System.Windows;
using System.Windows.Input;
using FrameForge.Analysis;
using FrameForge.App.Controls;
using FrameForge.App.Services;
using FrameForge.Imaging;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.App.Tests;

/// <summary>Develop's Masks section (Milestone 08 phase B, B5).</summary>
public sealed class MaskTests
{
    private static (EditStack Stack, PhotoTileViewModel Tile) Setup()
    {
        var stack = new EditStack();
        var tile = new PhotoTileViewModel(TestPhotos.Create("a.jpg"), new FakeThumbnailSource());
        return (stack, tile);
    }

    private static LocalAdjustment Subject(double exposure = 0.5) => new()
    {
        Id = Guid.NewGuid(),
        Mask = new MaskReference { Kind = MaskKind.Subject, Source = FakeMaskService.Source(MaskKind.Subject) },
        Exposure = exposure,
    };

    private static async Task SettleAsync(EditStack stack)
    {
        await stack.MaskList.WhenIdle;
        await stack.Develop.FlushAsync();
        await stack.Develop.RenderLoop;
    }

    // ------------------------------------------------------------------ the recipe keeps its masks

    [Fact]
    public async Task MovingAGlobalSlider_KeepsThePhotosMasks()
    {
        var (stack, tile) = Setup();
        var subject = Subject();
        tile.Recipe = EditRecipe.Neutral with { LocalAdjustments = new([subject]) };
        await stack.Develop.OpenAsync(tile);

        stack.Develop.Exposure = 0.4;
        await SettleAsync(stack);

        Assert.Equal(0.4, tile.Recipe.Exposure);
        Assert.Equal(subject, Assert.Single(tile.Recipe.LocalAdjustments));
    }

    [Fact]
    public async Task OpeningAPhotoWithMasks_ShowsThem_AndComputesTheirMasks()
    {
        var (stack, tile) = Setup();
        tile.Recipe = EditRecipe.Neutral with { LocalAdjustments = new([Subject()]) };

        await stack.Develop.OpenAsync(tile);
        await SettleAsync(stack);

        var item = Assert.Single(stack.MaskList.Items);
        Assert.Same(item, stack.MaskList.Selected);
        Assert.Equal("Subject", item.Name);
        Assert.Equal("1 person", item.StatusText);
        Assert.Single(stack.MaskList.ReadyMasks());
    }

    // ------------------------------------------------------------------ adding, editing, undo

    [Fact]
    public async Task AddSubject_ComputesTheMask_SavesIt_AndEachStepIsUndoable()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);

        stack.MaskList.AddSubjectCommand.Execute(null);
        await SettleAsync(stack);
        var item = Assert.Single(stack.MaskList.Items);
        Assert.Equal("1 person", item.Summary);
        Assert.True(stack.MaskList.ShowOverlay); // a new mask is shown, so you can see what was selected
        Assert.Single(tile.Recipe.LocalAdjustments);

        item.Exposure = 1;
        await SettleAsync(stack);
        Assert.Equal(1, Assert.Single(tile.Recipe.LocalAdjustments).Exposure);

        await stack.Develop.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, Assert.Single(tile.Recipe.LocalAdjustments).Exposure);
        await stack.Develop.UndoCommand.ExecuteAsync(null);
        Assert.Empty(tile.Recipe.LocalAdjustments);
        Assert.Empty(stack.MaskList.Items);
    }

    [Fact]
    public async Task Background_IsTheSubjectInverted()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);

        stack.MaskList.AddBackgroundCommand.Execute(null);
        await SettleAsync(stack);

        var stored = Assert.Single(tile.Recipe.LocalAdjustments).Mask;
        Assert.Equal((MaskKind.Subject, true, "Background"), (stored.Kind, stored.Invert, stack.MaskList.Items[0].Name));
    }

    [Fact]
    public async Task InvertAndDelete_AreSaved_AndInvertNeedsNoNewMask()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.MaskList.AddSkyCommand.Execute(null);
        await SettleAsync(stack);
        var item = stack.MaskList.Items[0];

        item.Invert = true;
        await SettleAsync(stack);
        Assert.True(Assert.Single(tile.Recipe.LocalAdjustments).Mask.Invert);
        Assert.Equal("Not sky", item.Name);
        Assert.Single(stack.Masks.Requests); // the same mask, used the other way round

        stack.MaskList.DeleteCommand.Execute(item);
        await SettleAsync(stack);
        Assert.Empty(tile.Recipe.LocalAdjustments);
        Assert.Null(stack.MaskList.Selected);
    }

    [Fact]
    public async Task AtMostEightMasks()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);

        for (var i = 0; i < EditRecipe.MaxLocalAdjustments; i++)
        {
            stack.MaskList.AddSkyCommand.Execute(null);
        }

        Assert.False(stack.MaskList.AddSkyCommand.CanExecute(null));
        Assert.Equal("8 of 8", stack.MaskList.CountText);
        await SettleAsync(stack);
    }

    // ------------------------------------------------------------------ unavailable, progress, cancel

    [Fact]
    public async Task WithoutTheModels_SubjectAndBackgroundAreOff_AndSayWhy_SkyStillWorks()
    {
        var (stack, tile) = Setup();
        stack.Masks.SubjectUnavailable = "Select subject needs the person detector. Install it in AI models.";

        await stack.Develop.OpenAsync(tile);

        Assert.False(stack.MaskList.AddSubjectCommand.CanExecute(null));
        Assert.False(stack.MaskList.AddBackgroundCommand.CanExecute(null));
        Assert.True(stack.MaskList.AddSkyCommand.CanExecute(null));
        Assert.Contains("Install it in AI models", stack.MaskList.SubjectUnavailable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaskNotFound_ShowsTheReason_AndTheRestOfTheEditStillRenders()
    {
        var (stack, tile) = Setup();
        stack.Masks.SkyNotFound = true;
        await stack.Develop.OpenAsync(tile);

        stack.MaskList.AddSkyCommand.Execute(null);
        stack.Develop.Exposure = 0.5;
        await SettleAsync(stack);

        var item = stack.MaskList.Items[0];
        Assert.True(item.IsUnavailable);
        Assert.Equal("No sky found. Click the sky to select it.", item.Unavailable);
        Assert.Empty(stack.MaskList.ReadyMasks());
        Assert.NotNull(stack.Develop.Rendered);
        Assert.Equal(0.5, tile.Recipe.Exposure);
    }

    [Fact]
    public async Task WhileComputing_TheRowSaysSo_AndCancelStopsIt()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Masks.Hold = new TaskCompletionSource();

        stack.MaskList.AddSkyCommand.Execute(null);
        var item = stack.MaskList.Items[0];
        Assert.True(item.IsComputing);
        Assert.Equal("Finding the sky…", item.StatusText);

        stack.MaskList.CancelComputingCommand.Execute(item);
        await stack.MaskList.WhenIdle;

        Assert.False(item.IsComputing);
        Assert.StartsWith("Cancelled", item.Unavailable, StringComparison.Ordinal);

        stack.Masks.Hold = null;
        stack.MaskList.RetryCommand.Execute(item);
        await SettleAsync(stack);
        Assert.Equal("Sky covers 50% of the photo", item.StatusText);
    }

    [Fact]
    public async Task MovingASliderWhileComputing_DoesNotRestartTheMask()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.Masks.Hold = new TaskCompletionSource();
        stack.MaskList.AddSkyCommand.Execute(null);
        var item = stack.MaskList.Items[0];

        item.Exposure = 0.3;
        item.Exposure = 0.6;
        stack.Masks.Hold.SetResult();
        await SettleAsync(stack);

        Assert.Single(stack.Masks.Requests);
        Assert.Equal("Sky covers 50% of the photo", item.StatusText);
    }

    // ------------------------------------------------------------------ Refine (clicks)

    [Fact]
    public async Task RefineClick_IsMappedThroughTheCrop_SavedWithTheMask_AndRecomputes()
    {
        var (stack, tile) = Setup();
        tile.Recipe = EditRecipe.Neutral with { Crop = new CropRect(0.5, 0, 0.5, 1) }; // the right half
        await stack.Develop.OpenAsync(tile);
        stack.MaskList.AddSubjectCommand.Execute(null);
        await SettleAsync(stack);

        stack.Develop.AddPointCommand.Execute(new ImagePoint(0.5, 0.5, Include: true)); // ignored: not refining
        Assert.Empty(stack.MaskList.Items[0].Points);

        stack.MaskList.ToggleRefineCommand.Execute(null);
        stack.Develop.AddPointCommand.Execute(new ImagePoint(0.5, 0.5, Include: true));
        stack.Develop.AddPointCommand.Execute(new ImagePoint(0.1, 0.9, Include: false));
        await SettleAsync(stack);

        var points = Assert.Single(tile.Recipe.LocalAdjustments).Mask.Points;
        Assert.Equal(2, points.Count);
        Assert.Equal(0.75, points[0].X, 3); // the middle of the right half
        Assert.Equal(0.5, points[0].Y, 3);
        Assert.False(points[1].Include);
        Assert.Equal(2, stack.Masks.Requests[^1].Points.Count);
        Assert.Equal("1 person (2 clicks)", stack.MaskList.Items[0].Summary);

        stack.MaskList.ClearClicksCommand.Execute(null);
        await SettleAsync(stack);
        Assert.Empty(Assert.Single(tile.Recipe.LocalAdjustments).Mask.Points);
    }

    [Fact]
    public async Task CropMode_AndRefine_TurnEachOtherOff()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.MaskList.AddSkyCommand.Execute(null);
        await SettleAsync(stack);

        stack.MaskList.ToggleRefineCommand.Execute(null);
        stack.Develop.ToggleCropModeCommand.Execute(null);
        Assert.False(stack.MaskList.IsRefining);

        stack.MaskList.ToggleRefineCommand.Execute(null);
        Assert.False(stack.Develop.IsCropMode);
    }

    // ------------------------------------------------------------------ overlay, presets, keys

    [Fact]
    public async Task Overlay_ChangesThePreview_AndHidingItRestoresIt()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.MaskList.AddSkyCommand.Execute(null);
        await SettleAsync(stack);
        var withOverlay = BitmapConversions.ToBuffer(stack.Develop.Rendered!);

        stack.MaskList.ToggleOverlayCommand.Execute(null);
        await SettleAsync(stack);
        var without = BitmapConversions.ToBuffer(stack.Develop.Rendered!);

        Assert.NotEqual(withOverlay.Bgra, without.Bgra);
        Assert.Equal(FakeEditRenderService.Source.Bgra, without.Bgra); // a mask with no settings changes nothing
    }

    [Fact]
    public async Task PresetWithMasks_IncludesTheMasksGroup()
    {
        var (stack, tile) = Setup();
        await stack.Develop.OpenAsync(tile);
        stack.MaskList.AddSkyCommand.Execute(null);
        await SettleAsync(stack);

        stack.Develop.NewPresetName = "Sky down";
        stack.Develop.PresetIncludesMasks = true;
        await stack.Develop.SavePresetCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(stack.Presets.Stored).Groups.HasFlag(PresetGroups.Masks));
    }

    [Theory]
    [InlineData(Key.O, CullingAction.ToggleMaskOverlay)]
    [InlineData(Key.M, CullingAction.ToggleRefine)]
    public void MaskKeys_AreMapped(Key key, CullingAction action) =>
        Assert.Equal(action, CullingShortcuts.Resolve(key, ModifierKeys.None));

    [Fact]
    public void ClickPosition_FollowsZoomAndPan()
    {
        // At 2 DIPs per pixel, panned 100 DIPs left: viewport x 300 is image x (300 + 100) / 2 = 200 of 400.
        var (u, v) = ZoomPanImage.ToImage(new Point(300, 50), new Point(-100, 0), 2, 400, 100);

        Assert.Equal((0.5, 0.25), (u, v));
    }

    [Fact]
    public void OverlayPainter_DarkensOutside_KeepsInside_AndMarksClicks()
    {
        var photo = AnalysisImage.Create(64, 48, (_, _) => (200, 180, 160));
        var leftHalf = new MaskImage(64, 48, Enumerable.Range(0, 64 * 48).Select(i => i % 64 < 32 ? (byte)255 : (byte)0).ToArray());

        var painted = MaskOverlayPainter.Paint(photo, EditRecipe.Neutral, photo, leftHalf, invert: false, [new MaskPoint(0.25, 0.5, Include: true)]);

        Assert.Equal(photo.Bgra[(((5 * 64) + 5) * 4) + 2], painted.Bgra[(((5 * 64) + 5) * 4) + 2]); // inside: unchanged
        Assert.True(painted.Bgra[(((5 * 64) + 60) * 4) + 2] < 80);                                  // outside: darkened
        Assert.Equal(255, painted.Bgra[(((24 * 64) + 16) * 4) + 2]);                                  // the + at the click
    }
}
