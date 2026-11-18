using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Masks;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Editing;

/// <summary>Rendering with local adjustments (B4): masks come from the mask service; a missing one is left out.</summary>
public sealed class EditRenderServiceTests
{
    private static readonly Photo Photo = new() { Id = Guid.NewGuid(), FilePath = "a.jpg", FileName = "a.jpg", FolderPath = ".", Width = 40, Height = 30, Status = ImportStatus.Imported };
    private static readonly AnalysisImage Grey = AnalysisImage.Create(40, 30, (_, _) => (90, 90, 90));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LocalAdjustment Local(MaskKind kind, double exposure) =>
        new() { Id = Guid.NewGuid(), Mask = new MaskReference { Kind = kind, Source = "x@1" }, Exposure = exposure };

    [Fact]
    public async Task Render_UsesTheMasksItCanGet_AndLeavesOutTheOthers()
    {
        var subject = Local(MaskKind.Subject, 1);
        var sky = Local(MaskKind.Sky, -1);
        var recipe = EditRecipe.Neutral with { LocalAdjustments = new([subject, sky]) };
        var service = new EditRenderService(new Loader(), new Masks(), NullLogger<EditRenderService>.Instance);

        var masks = await service.LoadMasksAsync(Photo, recipe, Ct);
        var output = await service.RenderAsync(Photo, recipe, null, Ct);

        Assert.Equal([subject.Id], masks.Masks.Keys);
        Assert.Equal((sky.Id, "No sky found."), Assert.Single(masks.Unavailable));
        Assert.True(output.Bgra[2] > 110); // the subject's +1 EV was applied over the whole (fully masked) photo
    }

    [Fact]
    public async Task AdjustmentsWithNoEffect_AndRecipesWithoutLocals_AskForNoMask()
    {
        var masks = new Masks();
        var service = new EditRenderService(new Loader(), masks, NullLogger<EditRenderService>.Instance);

        Assert.Same(LocalMasks.None, await service.LoadMasksAsync(Photo, EditRecipe.Neutral, Ct));
        var none = await service.LoadMasksAsync(Photo, EditRecipe.Neutral with { LocalAdjustments = new([Local(MaskKind.Subject, 0)]) }, Ct);

        Assert.Empty(none.Masks);
        Assert.Empty(none.Unavailable);
        Assert.Equal(0, masks.Requests);
    }

    private sealed class Loader : IAnalysisImageLoader
    {
        public AnalysisImage Load(string path, int maxPixelSize) => Grey;
    }

    /// <summary>Subject: a full mask. Sky: none found.</summary>
    private sealed class Masks : IMaskService
    {
        public int Requests { get; private set; }

        public string? CurrentSource(MaskKind kind) => "x@1";

        public string? UnavailableReason(MaskKind kind) => null;

        public MaskOutcome? TryGetCached(Photo photo, MaskReference reference) => null;

        public Task<MaskOutcome> GetAsync(Photo photo, MaskReference reference, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(reference.Kind == MaskKind.Subject
                ? new MaskOutcome { Mask = new MaskImage(8, 6, Enumerable.Repeat((byte)255, 48).ToArray()), Source = "x@1", Summary = "1 person" }
                : MaskOutcome.Unavailable("x@1", "No sky found."));
        }
    }
}
