using FrameForge.Application.Editing;
using FrameForge.Core.Editing;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Editing;

public sealed class EditServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly EditPreset Warm = new(Guid.NewGuid(), "Warm", EditRecipe.Neutral with { Temperature = 30, Exposure = 1 }, PresetGroups.WhiteBalance);

    [Fact]
    public async Task ApplyPreset_ChangesOnlyPresetGroups_ReportsProgress_AndRecordsBeforeAfter()
    {
        var repository = new MemoryEditRepository();
        var service = new EditService(repository, NullLogger<EditService>.Instance);
        var targets = Enumerable.Range(0, 250).Select(i => (Guid.NewGuid(), EditRecipe.Neutral with { Exposure = i % 2 == 0 ? 0.5 : 0 })).ToList();
        var progress = new List<BatchProgress>();

        var result = await service.ApplyPresetAsync(targets, Warm, new SyncProgress<BatchProgress>(progress.Add), Ct);

        Assert.False(result.WasCancelled);
        Assert.Equal(250, result.Changes.Count);
        Assert.All(result.Changes, c =>
        {
            Assert.Equal(30, c.After.Temperature);
            Assert.Equal(c.Before.Exposure, c.After.Exposure); // exposure is not in the preset's groups
        });
        Assert.Equal(250, repository.Saved.Count);
        Assert.Equal(new BatchProgress(250, 250), progress[^1]);
        Assert.Equal(3, repository.SaveCalls); // batches of 100
    }

    [Fact]
    public async Task ApplyPreset_Cancelled_KeepsAndReportsCompletedBatches()
    {
        var repository = new MemoryEditRepository();
        var service = new EditService(repository, NullLogger<EditService>.Instance);
        var targets = Enumerable.Range(0, 300).Select(_ => (Guid.NewGuid(), EditRecipe.Neutral)).ToList();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var result = await service.ApplyPresetAsync(targets, Warm, new SyncProgress<BatchProgress>(p =>
        {
            if (p.Completed == 100)
            {
                cts.Cancel();
            }
        }), cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(100, result.Changes.Count);
        Assert.Equal(100, repository.Saved.Count);
    }

    [Fact]
    public async Task ApplyPreset_RejectsInvalidPreset()
    {
        var service = new EditService(new MemoryEditRepository(), NullLogger<EditService>.Instance);
        var bad = Warm with { Groups = PresetGroups.None };
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyPresetAsync([(Guid.NewGuid(), EditRecipe.Neutral)], bad, null, Ct));
    }

    [Fact]
    public void History_UndoRedo_RestoresExactRecipes()
    {
        var history = new EditHistory();
        var a = EditRecipe.Neutral;
        var b = a with { Exposure = 1 };
        var c = b with { Contrast = 20 };
        history.Record(a);
        history.Record(b);

        Assert.Equal(b, history.Undo(c));
        Assert.Equal(a, history.Undo(b));
        Assert.Null(history.Undo(a));
        Assert.Equal(b, history.Redo(a));
        Assert.Equal(c, history.Redo(b));
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_NewChangeClearsRedo_AndIsBounded()
    {
        var history = new EditHistory(capacity: 3);
        for (var i = 0; i < 10; i++)
        {
            history.Record(EditRecipe.Neutral with { Exposure = i / 10.0 });
        }

        var undone = 0;
        var current = EditRecipe.Neutral;
        while (history.Undo(current) is { } previous)
        {
            current = previous;
            undone++;
        }

        Assert.Equal(3, undone);
        history.Record(EditRecipe.Neutral);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RecipeJson_RoundTrips_AndRejectsGarbage()
    {
        var recipe = EditRecipe.Neutral with { Exposure = -1.25, Crop = new CropRect(0.1, 0.2, 0.3, 0.4), QuarterTurns = 2 };
        Assert.Equal(recipe, RecipeJson.TryDeserialize(RecipeJson.Serialize(recipe), out _));
        Assert.Null(RecipeJson.TryDeserialize("not json", out var error));
        Assert.NotNull(error);
        Assert.Null(RecipeJson.TryDeserialize("{\"SchemaVersion\": 99}", out _));
    }

    private sealed class MemoryEditRepository : IEditRepository
    {
        public Dictionary<Guid, EditRecipe> Saved { get; } = [];

        public int SaveCalls { get; private set; }

        public Task<StoredEdits> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredEdits(new Dictionary<Guid, EditRecipe>(Saved), []));

        public Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            foreach (var (id, recipe) in recipes)
            {
                Saved[id] = recipe;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
