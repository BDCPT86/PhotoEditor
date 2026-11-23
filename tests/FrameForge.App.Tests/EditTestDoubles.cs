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

    public Task<LocalMasks> LoadMasksAsync(Photo photo, EditRecipe recipe, CancellationToken cancellationToken, bool cachedOnly = false) => Task.FromResult(LocalMasks.None);
}

/// <summary>Face boxes per photo; photos absent from <see cref="Analysed"/> have no face data.</summary>
internal sealed class FakeFaceLookup : FrameForge.Application.Editing.Auto.IFaceLookup
{
    public Dictionary<Guid, List<FrameForge.Application.Ai.FaceFinding>> Analysed { get; } = [];

    public double FaceMinScore => 0.6;

    public bool HasFaceData(Guid photoId) => Analysed.ContainsKey(photoId);

    public IReadOnlyList<FrameForge.Application.Ai.FaceFinding> FacesOf(Guid photoId) => Analysed.GetValueOrDefault(photoId) ?? [];
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
        Controller = new EditController(new EditService(Edits, NullLogger<EditService>.Instance), Presets, Renderer, Faces, Masks, NullLogger<EditController>.Instance);
        MaskList = new MaskListViewModel(Masks, NullLogger<MaskListViewModel>.Instance);
        Develop = new DevelopViewModel(Renderer, Controller, Faces, MaskList, NullLogger<DevelopViewModel>.Instance) { CommitDelay = TimeSpan.Zero };
    }

    public MemoryEditRepository Edits { get; } = new();

    public MemoryPresetRepository Presets { get; } = new();

    public FakeEditRenderService Renderer { get; } = new();

    public FakeFaceLookup Faces { get; } = new();

    public FakeMaskService Masks { get; } = new();

    public MaskListViewModel MaskList { get; }

    public EditController Controller { get; }

    public DevelopViewModel Develop { get; }
}

/// <summary>
/// Masks without models: Subject is the left half of the photo, Sky the top half. A request can be held open with
/// <see cref="Hold"/> (to test progress and cancel), and Subject can be made unavailable.
/// </summary>
internal sealed class FakeMaskService : FrameForge.Application.Editing.Masks.IMaskService
{
    public string? SubjectUnavailable { get; set; }

    public bool SkyNotFound { get; set; }

    /// <summary>When set, requests wait for it (or for cancellation) before answering.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public List<MaskReference> Requests { get; } = [];

    public static string Source(MaskKind kind) => kind == MaskKind.Subject ? "fake-subject@1" : "fake-sky@1";

    public string? CurrentSource(MaskKind kind) => Source(kind);

    public string? UnavailableReason(MaskKind kind) => kind == MaskKind.Subject ? SubjectUnavailable : null;

    public FrameForge.Application.Editing.Masks.MaskOutcome? TryGetCached(Photo photo, MaskReference reference) => null;

    public async Task<FrameForge.Application.Editing.Masks.MaskOutcome> GetAsync(Photo photo, MaskReference reference, CancellationToken cancellationToken)
    {
        Requests.Add(reference);
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Kind == MaskKind.Subject && SubjectUnavailable is { } reason)
        {
            return FrameForge.Application.Editing.Masks.MaskOutcome.Unavailable(Source(reference.Kind), reason);
        }

        if (reference.Kind == MaskKind.Sky && SkyNotFound)
        {
            return FrameForge.Application.Editing.Masks.MaskOutcome.Unavailable(Source(reference.Kind), "No sky found. Click the sky to select it.");
        }

        const int w = 32, h = 24;
        var alpha = new byte[w * h];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = reference.Kind == MaskKind.Subject ? (i % w < w / 2 ? (byte)255 : (byte)0) : (i / w < h / 2 ? (byte)255 : (byte)0);
        }

        var summary = reference.Kind == MaskKind.Subject ? $"1 person{(reference.Points.Count > 0 ? $" ({reference.Points.Count} clicks)" : string.Empty)}" : "Sky covers 50% of the photo";
        return new FrameForge.Application.Editing.Masks.MaskOutcome { Mask = new MaskImage(w, h, alpha), Source = Source(reference.Kind), Summary = summary };
    }
}
