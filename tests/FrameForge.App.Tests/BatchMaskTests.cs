using FrameForge.App.ViewModels;
using FrameForge.Application.Export;
using FrameForge.Core.Editing;

namespace FrameForge.App.Tests;

/// <summary>Presets with masks on many photos, and exports that leave a mask out (Milestone 08 phase B, B6).</summary>
public sealed class BatchMaskTests
{
    private static EditPreset LiftSubjectCoolSky() => new(
        Guid.NewGuid(),
        "Lift subject, cool sky",
        EditRecipe.Neutral with
        {
            Contrast = 10,
            LocalAdjustments = new(
            [
                new LocalAdjustment { Id = Guid.NewGuid(), Mask = new MaskReference { Kind = MaskKind.Subject, Source = "old-subject@0" }, Exposure = 0.5 },
                new LocalAdjustment { Id = Guid.NewGuid(), Mask = new MaskReference { Kind = MaskKind.Sky, Source = FakeMaskService.Source(MaskKind.Sky) }, Temperature = -10 },
            ]),
        },
        PresetGroups.Tone | PresetGroups.Masks);

    private static async Task<CullingFixture> OpenPresetsAsync(EditPreset preset, Action<FakeMaskService>? setup = null)
    {
        var f = new CullingFixture();
        setup?.Invoke(f.Edit.Masks);
        f.Edit.Presets.Stored.Add(preset);
        await f.LoadAsync();
        f.Select(0, 1, 2);
        f.Library.OpenPresetsCommand.Execute(null);
        f.PresetPanel.SelectedPreset = f.PresetPanel.Presets.Single();
        return f;
    }

    [Fact]
    public async Task PresetWithMasks_WorksOutEachPhotosMasks_LeavesOutTheOnesThatFail_AndUndoes()
    {
        using var f = await OpenPresetsAsync(LiftSubjectCoolSky(), m => m.SkyNotFound = true);

        await f.PresetPanel.ApplyCommand.ExecuteAsync(null);

        foreach (var i in Enumerable.Range(0, 3))
        {
            var recipe = f.Tile(i).Recipe;
            Assert.Equal(10, recipe.Contrast);
            var subject = Assert.Single(recipe.LocalAdjustments);           // the sky was left out
            Assert.Equal(MaskKind.Subject, subject.Mask.Kind);
            Assert.Equal(FakeMaskService.Source(MaskKind.Subject), subject.Mask.Source); // the source that made it
        }

        Assert.True(f.Tile(3).Recipe.IsNeutral);                             // not selected
        Assert.Equal(6, f.Edit.Masks.Requests.Count);                        // two masks for each of three photos
        Assert.Equal(3, f.PresetPanel.Notes.Count);
        Assert.StartsWith("IMG_00.jpg: Sky left out: No sky found", f.PresetPanel.Notes[0], StringComparison.Ordinal);
        Assert.Contains("3 got the preset without a mask", f.PresetPanel.ResultText, StringComparison.Ordinal);

        await f.PresetPanel.UndoBatchCommand.ExecuteAsync(null);
        Assert.All(Enumerable.Range(0, 3), i => Assert.True(f.Tile(i).Recipe.IsNeutral));
    }

    [Fact]
    public async Task WithoutTheSubjectModels_APresetWithASubjectMaskIsRefusedUpFront()
    {
        using var f = await OpenPresetsAsync(LiftSubjectCoolSky(), m => m.SubjectUnavailable = "Select subject needs the person detector. Install it in AI models.");

        await f.PresetPanel.ApplyCommand.ExecuteAsync(null);

        Assert.True(f.PresetPanel.ResultIsWarning);
        Assert.Contains("can't be made yet: Select subject needs", f.PresetPanel.ResultText, StringComparison.Ordinal);
        Assert.Empty(f.Edit.Masks.Requests);
        Assert.All(Enumerable.Range(0, 3), i => Assert.True(f.Tile(i).Recipe.IsNeutral));
    }

    [Fact]
    public async Task PresetWithoutMasks_StaysOnTheFastPath()
    {
        using var f = await OpenPresetsAsync(new EditPreset(Guid.NewGuid(), "Warm", EditRecipe.Neutral with { Temperature = 40 }, PresetGroups.WhiteBalance));

        Assert.Null(f.PresetPanel.EstimateText);
        await f.PresetPanel.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(f.Edit.Masks.Requests);
        Assert.Equal(40, f.Tile(0).Recipe.Temperature);
    }

    [Fact]
    public async Task PresetWithASubjectMask_SaysHowLongItWillTake_BeforeItStarts()
    {
        using var f = await OpenPresetsAsync(LiftSubjectCoolSky());

        // 3 photos × (0.6–3 s subject + 0.2–0.6 s sky) = 2.4–10.8 s: under a minute either way.
        Assert.Equal("Has masks: about 1 min for 3 photo(s), because each photo's subject is found (0.6–3 s each). You can cancel and keep what's done.", f.PresetPanel.EstimateText);
    }

    [Theory]
    [InlineData(2, 200, 60, "2 of 200")]                                   // too early to say
    [InlineData(40, 200, 120, "40 of 200 · about 8 min left")]             // 3 s per photo, 160 to go
    [InlineData(195, 200, 600, "195 of 200 · under a minute left")]
    [InlineData(200, 200, 600, "200 of 200")]
    public void Countdown_UsesTheSpeedSoFar(int done, int total, int seconds, string expected) =>
        Assert.Equal(expected, PresetPanelViewModel.Countdown(done, total, TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(30, 70, "1–2 min")]
    [InlineData(400, 1000, "7–17 min")]
    [InlineData(3000, 7500, "50 min–2 h 5 min")]
    public void EstimateRange_IsReadable(int low, int high, string expected) =>
        Assert.Equal(expected, PresetPanelViewModel.FormatRange((TimeSpan.FromSeconds(low), TimeSpan.FromSeconds(high))));

    [Fact]
    public void ExportSummary_ListsPhotosSavedWithoutAMask()
    {
        var text = ExportViewModel.Describe(
            new ExportSummary
            {
                Destination = @"D:\Out",
                Requested = 2,
                Exported = 2,
                Notes = [new ExportNote(@"C:\A\IMG_0605.JPG", "Sky left out: No sky found.")],
            },
            manifest: false,
            edited: true);

        Assert.Contains("Saved 2 edited JPEG(s)", text, StringComparison.Ordinal);
        Assert.Contains("1 saved without a mask: IMG_0605.JPG (Sky left out: No sky found.)", text, StringComparison.Ordinal);
    }
}
