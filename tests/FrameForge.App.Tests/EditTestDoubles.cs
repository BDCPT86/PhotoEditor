using System.IO;
using FrameForge.Analysis;
using FrameForge.App.Services;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

internal sealed class MemoryEditRepository : IEditRepository
{
    public Dictionary<Guid, EditRecipe> Stored { get; } = [];

    public bool FailSaves { get; set; }

    public int SaveCalls { get; private set; }

    public Task<StoredEdits> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StoredEdits(new Dictionary<Guid, EditRecipe>(Stored), []));

    public async Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        SaveCalls++;
        if (FailSaves)
        {
            throw new IOException("disk full (test)");
        }

        foreach (var (id, recipe) in recipes)
        {
            if (recipe.IsNeutral)
            {
                Stored.Remove(id);
            }
            else
            {
                Stored[id] = recipe.EnsureValid();
            }
        }
    }
}

internal sealed class MemoryPresetRepository : IPresetRepository
{
    public List<EditPreset> Stored { get; } = [];

    public Task<StoredPresets> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StoredPresets(Stored.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(), []));

    public Task SaveAsync(EditPreset preset, CancellationToken cancellationToken = default)
    {
        if (Stored.Any(p => p.Id != preset.Id && string.Equals(p.Name, preset.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicatePresetNameException($"A preset called “{preset.Name}” already exists.");
        }

        Stored.RemoveAll(p => p.Id == preset.Id);
        Stored.Add(preset with { Name = preset.Name.Trim() });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Stored.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>"Decodes" every photo as the same synthetic gradient and renders with the real renderer.</summary>
internal sealed class FakeEditRenderService : IEditRenderService
{
    public static readonly AnalysisImage Source = AnalysisImage.Create(96, 64, (x, y) => ((byte)(60 + x), (byte)(50 + (2 * y)), (byte)(120 - y)));

    public List<EditRecipe> Rendered { get; } = [];

    public int SourceLoads { get; private set; }

    public Task<AnalysisImage> LoadSourceAsync(Photo photo, int? maxLongSide, CancellationToken cancellationToken)
    {
        SourceLoads++;
        return Task.FromResult(Source);
    }

    public Task<AnalysisImage> RenderAsync(Photo photo, EditRecipe recipe, int? maxLongSide, CancellationToken cancellationToken)
    {
        lock (Rendered)
        {
            Rendered.Add(recipe);
        }

        return Task.FromResult(EditRenderer.Render(Source, recipe, maxLongSide, cancellationToken));
    }
}

internal sealed class RecordingEditedExporter : IEditedExportService
{
    public List<(IReadOnlyList<EditedExportItem> Items, EditedExportOptions Options)> Calls { get; } = [];

    public Task<ExportSummary> ExportAsync(IReadOnlyList<EditedExportItem> items, string destinationFolder, EditedExportOptions options, IProgress<ExportProgress>? progress, CancellationToken cancellationToken)
    {
        Calls.Add((items, options));
        return Task.FromResult(new ExportSummary { Destination = destinationFolder, Requested = items.Count, Exported = items.Count });
    }
}

/// <summary>Edit controller + Develop view model over in-memory storage (commits immediately).</summary>
internal sealed class EditStack
{
    public EditStack()
    {
        Controller = new EditController(new EditService(Edits, NullLogger<EditService>.Instance), Presets, NullLogger<EditController>.Instance);
        Develop = new DevelopViewModel(Renderer, Controller, NullLogger<DevelopViewModel>.Instance) { CommitDelay = TimeSpan.Zero };
    }

    public MemoryEditRepository Edits { get; } = new();

    public MemoryPresetRepository Presets { get; } = new();

    public FakeEditRenderService Renderer { get; } = new();

    public EditController Controller { get; }

    public DevelopViewModel Develop { get; }
}
