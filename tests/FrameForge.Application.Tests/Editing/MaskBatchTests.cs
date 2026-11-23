using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;

namespace FrameForge.Application.Tests.Editing;

/// <summary>One photo's step when a preset with masks is applied to many (B6).</summary>
public sealed class MaskBatchTests
{
    private static readonly Photo Photo = new() { Id = Guid.NewGuid(), FilePath = @"C:\Shoot\a.jpg", FileName = "a.jpg", FolderPath = @"C:\Shoot", Status = ImportStatus.Imported };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LocalAdjustment Local(MaskKind kind, double exposure, bool withClick = false) => new()
    {
        Id = Guid.NewGuid(),
        Mask = new MaskReference
        {
            Kind = kind,
            Source = "old@1",
            Points = withClick ? new([new MaskPoint(0.5, 0.5, true)]) : ValueList.Empty<MaskPoint>(),
        },
        Exposure = exposure,
    };

    private static EditPreset Preset(PresetGroups groups, params LocalAdjustment[] locals) =>
        new(Guid.NewGuid(), "P", EditRecipe.Neutral with { Exposure = 0.3, LocalAdjustments = new(locals) }, groups);

    [Fact]
    public void HasMasks_OnlyWithTheMasksGroup_AndASettingToApply()
    {
        Assert.True(MaskBatch.HasMasks(Preset(PresetGroups.Masks, Local(MaskKind.Sky, 0.5))));
        Assert.False(MaskBatch.HasMasks(Preset(PresetGroups.Exposure, Local(MaskKind.Sky, 0.5))));
        Assert.False(MaskBatch.HasMasks(Preset(PresetGroups.Masks, Local(MaskKind.Sky, 0))));
    }

    [Fact]
    public void Estimate_AddsUpTheMaskKinds()
    {
        var preset = Preset(PresetGroups.Masks, Local(MaskKind.Subject, 1), Local(MaskKind.Sky, 1));

        var (low, high) = MaskBatch.Estimate(preset, 100)!.Value;

        Assert.Equal(TimeSpan.FromSeconds(230), low);   // 100 × (2 + 0.3)
        Assert.Equal(TimeSpan.FromSeconds(600), high);  // 100 × (5 + 1)
        Assert.Null(MaskBatch.Estimate(Preset(PresetGroups.Exposure), 100));
    }

    [Fact]
    public async Task EachMaskIsWorkedOut_FailuresAreLeftOutAndNoted_ClicksAreNotCopied()
    {
        var masks = new Masks { SkyReason = "No sky found." };
        var preset = Preset(PresetGroups.Exposure | PresetGroups.Masks, Local(MaskKind.Subject, 0.5, withClick: true), Local(MaskKind.Sky, -0.5));

        var (after, note) = await MaskBatch.ApplyAsync(Photo, EditRecipe.Neutral, preset, masks, Ct);

        Assert.NotNull(after);
        Assert.Equal(0.3, after.Exposure);
        var subject = Assert.Single(after.LocalAdjustments);
        Assert.Equal("subject@2", subject.Mask.Source);   // follows what made it, so the cache matches
        Assert.Empty(subject.Mask.Points);
        Assert.Equal("Sky left out: No sky found.", note);
        Assert.All(masks.Requested, r => Assert.Empty(r.Points));
    }

    [Fact]
    public async Task AMaskWithNoSettings_IsKept_WithoutWorkingItOut()
    {
        var masks = new Masks();
        var preset = Preset(PresetGroups.Masks, Local(MaskKind.Subject, 0), Local(MaskKind.Sky, 0.4));

        var (after, note) = await MaskBatch.ApplyAsync(Photo, EditRecipe.Neutral, preset, masks, Ct);

        Assert.Equal(2, after!.LocalAdjustments.Count);
        Assert.Equal(MaskKind.Sky, Assert.Single(masks.Requested).Kind);
        Assert.Null(note);
    }

    [Fact]
    public async Task NothingChanged_IsReportedAsNoChange()
    {
        var current = EditRecipe.Neutral with { Exposure = 0.3 };

        var (after, _) = await MaskBatch.ApplyAsync(Photo, current, Preset(PresetGroups.Exposure), new Masks(), Ct);

        Assert.Null(after);
    }

    [Fact]
    public async Task Cancelling_StopsTheStep()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MaskBatch.ApplyAsync(Photo, EditRecipe.Neutral, Preset(PresetGroups.Masks, Local(MaskKind.Sky, 1)), new Masks(), cts.Token));
    }

    /// <summary>Subject masks come from "subject@2"; Sky fails when <see cref="SkyReason"/> is set.</summary>
    private sealed class Masks : IMaskService
    {
        public string? SkyReason { get; init; }

        public List<MaskReference> Requested { get; } = [];

        public string? CurrentSource(MaskKind kind) => kind == MaskKind.Subject ? "subject@2" : "sky@1";

        public string? UnavailableReason(MaskKind kind) => null;

        public MaskOutcome? TryGetCached(Photo photo, MaskReference reference) => null;

        public Task<MaskOutcome> GetAsync(Photo photo, MaskReference reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requested.Add(reference);
            var source = CurrentSource(reference.Kind)!;
            return Task.FromResult(reference.Kind == MaskKind.Sky && SkyReason is { } reason
                ? MaskOutcome.Unavailable(source, reason)
                : new MaskOutcome { Mask = new MaskImage(4, 3, new byte[12]), Source = source, Summary = "found" });
        }
    }
}
