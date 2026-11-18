using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing;
using FrameForge.Application.Editing.Auto;
using FrameForge.Application.Tests.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Editing;

public sealed class AutoBatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Renderer(AnalysisImage image) : IEditRenderService
    {
        public Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken) => Task.FromResult(image);

        public Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken) =>
            Task.FromResult(EditRenderer.Render(image, recipe, maxLongSide, cancellationToken));

        public Task<LocalMasks> LoadMasksAsync(Photo photo, EditRecipe recipe, CancellationToken cancellationToken, bool cachedOnly = false) => Task.FromResult(LocalMasks.None);
    }

    private sealed class Faces(Dictionary<Guid, List<FaceFinding>> data) : IFaceLookup
    {
        public double FaceMinScore => 0.6;

        public bool HasFaceData(Guid photoId) => data.ContainsKey(photoId);

        public IReadOnlyList<FaceFinding> FacesOf(Guid photoId) => data.GetValueOrDefault(photoId) ?? [];
    }

    private static Photo Photo()
    {
        var p = AnalysisTestPhotos.Create("a.jpg");
        p.Width = 6000;
        p.Height = 4000;
        return p;
    }

    private static readonly AnalysisImage Dim = AnalysisImage.Create(300, 200, (x, y) => ((byte)(70 + (x % 9)), (byte)(80 + (y % 7)), 70));

    [Fact]
    public async Task Light_Level_Crop_AreChained_LightFirst_CropLast()
    {
        var photo = Photo();
        var faces = new Faces(new() { [photo.Id] = [new FaceFinding(0.6f, 0.25f, 0.08f, 0.12f, 0.9f, 150)] });
        var options = new AutoBatchOptions { Light = "Natural", Crop = "Medium", CropAspect = "4:5" };

        var (after, note) = await AutoBatch.ApplyAsync(photo, EditRecipe.Neutral, options, new Renderer(Dim), faces, Ct);

        Assert.NotNull(after);
        Assert.Null(note);
        Assert.True(after!.Exposure > 0);
        Assert.False(after.Crop.IsFull);
        Assert.Equal(0.8, after.Crop.Width * 6000 / (after.Crop.Height * 4000), 2);
    }

    [Fact]
    public async Task MissingFaces_IsNoted_ButLightStillApplies()
    {
        var options = new AutoBatchOptions { Light = "Brighter", Crop = "Tight" };

        var (after, note) = await AutoBatch.ApplyAsync(Photo(), EditRecipe.Neutral, options, new Renderer(Dim), new Faces([]), Ct);

        Assert.True(after!.Exposure > 0);
        Assert.True(after.Crop.IsFull);
        Assert.Contains("not analysed for faces", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LooselyFramedPhoto_IsNotCropped_AndSaysWhy()
    {
        var photo = Photo();
        var faces = new Faces(new() { [photo.Id] = [new FaceFinding(0.45f, 0.2f, 0.08f, 0.12f, 0.9f, 150)] });

        var (after, note) = await AutoBatch.ApplyAsync(photo, EditRecipe.Neutral, new AutoBatchOptions { Crop = "Medium" }, new Renderer(Dim), faces, Ct);

        Assert.Null(after);
        Assert.Equal("not cropped (already framed loosely)", note);
    }

    [Fact]
    public async Task NothingToChange_ReturnsNull()
    {
        var level = AnalysisImage.Create(300, 200, (_, y) => y < 100 ? ((byte)200, (byte)200, (byte)200) : ((byte)60, (byte)60, (byte)60));
        var (after, _) = await AutoBatch.ApplyAsync(Photo(), EditRecipe.Neutral, new AutoBatchOptions { Level = true }, new Renderer(level), new Faces([]), Ct);
        Assert.Null(after); // already level: no change, no note
    }

    [Fact]
    public async Task EditService_GenericBatch_SavesOnlyChanges_KeepsNotes_AndHonoursCancel()
    {
        var repository = new MemoryRepo();
        var service = new EditService(repository, NullLogger<EditService>.Instance);
        var ids = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var seen = 0;

        var result = await service.ApplyAsync(
            ids.Select(id => (id, EditRecipe.Neutral)).ToList(),
            (id, current, _) =>
            {
                seen++;
                if (seen == 40)
                {
                    cts.Cancel();
                }

                return Task.FromResult<(EditRecipe?, string?)>(seen % 2 == 0 ? (current with { Exposure = 1 }, null) : (null, "skipped"));
            },
            null,
            cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(20, result.Changes.Count);   // even photos among the 40 processed
        Assert.Equal(20, repository.Saved.Count); // saved, including the last partial batch
        Assert.Equal(20, result.Notes.Count);
    }

    private sealed class MemoryRepo : IEditRepository
    {
        public Dictionary<Guid, EditRecipe> Saved { get; } = [];

        public Task<StoredEdits> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StoredEdits(Saved, []));

        public Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default)
        {
            foreach (var (id, r) in recipes)
            {
                Saved[id] = r;
            }

            return Task.CompletedTask;
        }
    }
}
