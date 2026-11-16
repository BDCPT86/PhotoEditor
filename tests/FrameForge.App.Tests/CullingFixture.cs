using System.IO;
using FrameForge.App.Services;
using FrameForge.App.ViewModels;
using FrameForge.Application.Analysis;
using FrameForge.Application.Decisions;
using FrameForge.Application.Export;
using FrameForge.Application.Photos;
using FrameForge.Application.Settings;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

/// <summary>A real LibraryViewModel wired to real decision/filter/viewer/compare logic, with in-memory storage.</summary>
internal sealed class CullingFixture : IDisposable
{
    public CullingFixture(int photoCount = 6, bool autoAdvance = true)
    {
        Photos = Enumerable.Range(0, photoCount).Select(i => TestPhotos.Create($"IMG_{i:00}.jpg")).ToList();
        for (var i = 0; i < Photos.Count; i++)
        {
            Photos[i].CaptureLocalTime = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Unspecified).AddSeconds(i);
        }
        Settings = new SettingsService(SettingsStore, NullLogger<SettingsService>.Instance);
        Settings.SetAsync(AppSettings.ViewerAutoAdvance, autoAdvance).GetAwaiter().GetResult();

        DecisionService = new DecisionService(Decisions);
        Controller = new DecisionController(DecisionService, NullLogger<DecisionController>.Instance);
        Viewer = new ViewerViewModel(Loader, NullLogger<ViewerViewModel>.Instance);
        Compare = new CompareViewModel(Loader, NullLogger<CompareViewModel>.Instance);
        Export = new ExportViewModel(Exporter, new NullFolderPicker(), new NullSavePicker(), NullLogger<ExportViewModel>.Instance);
        AnalysisController = new AnalysisController(AnalysisService, AiService, AiRuntime, Settings);
        AnalysisPanel = new LibraryAnalysisViewModel(AnalysisController, NullLogger<LibraryAnalysisViewModel>.Instance);

        Library = new LibraryViewModel(
            new FixedLibrary(Photos),
            new UnusedImporter(),
            DecisionService,
            Settings,
            new NullFolderPicker(),
            new FakeThumbnailSource(),
            Controller,
            new LibraryFilterViewModel(),
            Viewer,
            Compare,
            Export,
            AnalysisPanel,
            NullLogger<LibraryViewModel>.Instance);
    }

    public FakeAnalysisService AnalysisService { get; } = new();

    public FakeAiAnalysisService AiService { get; } = new();

    public FakeAiRuntime AiRuntime { get; } = new();

    public AnalysisController AnalysisController { get; }

    public LibraryAnalysisViewModel AnalysisPanel { get; }

    public List<Photo> Photos { get; }

    public MemorySettingsStore SettingsStore { get; } = new();

    public SettingsService Settings { get; }

    public MemoryDecisionRepository Decisions { get; } = new();

    public DecisionService DecisionService { get; }

    public DecisionController Controller { get; }

    public ScriptedBitmapLoader Loader { get; } = new();

    public ViewerViewModel Viewer { get; }

    public CompareViewModel Compare { get; }

    public RecordingExporter Exporter { get; } = new();

    public ExportViewModel Export { get; }

    public LibraryViewModel Library { get; }

    public PhotoTileViewModel Tile(int index) => Library.Photos.Single(t => t.FileName == $"IMG_{index:00}.jpg");

    public async Task LoadAsync() => await Library.OnNavigatedToAsync(TestContext.Current.CancellationToken);

    public void Select(params int[] indexes)
    {
        var tiles = indexes.Select(Tile).ToList();
        Library.SelectedPhoto = tiles.FirstOrDefault();
        ((Controls.ISelectedItemsSink)Library).UpdateSelection(tiles);
    }

    /// <summary>Waits until the background save has reached the repository.</summary>
    public async Task<PhotoDecision> StoredAsync(int index, Func<PhotoDecision, bool> until)
    {
        var id = Photos[index].Id;
        for (var i = 0; i < 100; i++)
        {
            var stored = Decisions.Get(id);
            if (until(stored))
            {
                return stored;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return Decisions.Get(id);
    }

    public void Dispose()
    {
        Viewer.Dispose();
        Compare.Dispose();
        DecisionService.Dispose();
    }

    private sealed class FixedLibrary(List<Photo> photos) : IPhotoLibraryService
    {
        public Task<IReadOnlyList<Photo>> GetPhotosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Photo>>(photos);
    }

    private sealed class UnusedImporter : IPhotoImportService
    {
        public Task<ImportSummary> ImportFolderAsync(string folderPath, bool includeSubfolders, IProgress<ImportProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NullFolderPicker : IFolderPicker
    {
        public string? PickFolder(string title, string? initialFolder) => null;
    }

    private sealed class NullSavePicker : IFileSavePicker
    {
        public string? PickSaveFile(string title, string defaultFileName, string filter) => null;
    }
}

/// <summary>Analysis service returning scripted results; counts real analysis runs.</summary>
internal sealed class FakeAnalysisService : IPhotoAnalysisService
{
    public Dictionary<Guid, PhotoAnalysis> Results { get; } = [];

    public int AnalyzeCalls { get; private set; }

    public int LoadCalls { get; private set; }

    /// <summary>Gives a photo a full analysis with the given perceptual hash and sharpness.</summary>
    public void Set(Photo photo, ulong hash, double sharpness = 200, string? content = null) =>
        Results[photo.Id] = new PhotoAnalysis
        {
            ContentHash = content ?? "unique-" + photo.FileName,
            PerceptualHash = hash,
            Sharpness = new FrameForge.Analysis.SharpnessResult(sharpness, sharpness / 2, 4, 0, 0, new double[16], 1024, 683),
            Exposure = new FrameForge.Analysis.ExposureResult(120, 0, 0, new int[16]),
        };

    public Task<IReadOnlyDictionary<Guid, PhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, FrameForge.Analysis.AnalysisSettings settings, CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        return Task.FromResult<IReadOnlyDictionary<Guid, PhotoAnalysis>>(new Dictionary<Guid, PhotoAnalysis>(Results));
    }

    public Task<AnalysisRunSummary> AnalyzeAsync(IReadOnlyList<Photo> photos, FrameForge.Analysis.AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        AnalyzeCalls++;
        return Task.FromResult(new AnalysisRunSummary { Analysed = photos.Count - Results.Count, Cached = Results.Count });
    }
}

/// <summary>AI service returning scripted results. With no models (default) a run reports every photo as not eligible.</summary>
internal sealed class FakeAiAnalysisService : FrameForge.Application.Ai.IAiAnalysisService
{
    public Dictionary<Guid, FrameForge.Application.Ai.AiPhotoAnalysis> Results { get; } = [];

    public bool ModelsAvailable { get; set; }

    public int AnalyzeCalls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, FrameForge.Application.Ai.AiPhotoAnalysis>> LoadAsync(IReadOnlyList<Photo> photos, FrameForge.Application.Ai.AiCapabilities capabilities, FrameForge.Analysis.AnalysisSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, FrameForge.Application.Ai.AiPhotoAnalysis>>(
            capabilities.Any ? new Dictionary<Guid, FrameForge.Application.Ai.AiPhotoAnalysis>(Results) : []);

    public Task<AnalysisRunSummary> AnalyzeAsync(IReadOnlyList<Photo> photos, FrameForge.Application.Ai.AiCapabilities capabilities, FrameForge.Analysis.AnalysisSettings settings, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        AnalyzeCalls++;
        return Task.FromResult(ModelsAvailable
            ? new AnalysisRunSummary { Analysed = photos.Count - Results.Count, Cached = Results.Count }
            : new AnalysisRunSummary { NotEligible = photos.Count });
    }
}

internal sealed class FakeAiRuntime : FrameForge.Application.Ai.IAiRuntime
{
    public string? LastError { get; set; }

    public int Resets { get; private set; }

    public FrameForge.Application.Ai.IFaceDetector? GetFaceDetector() => null;

    public FrameForge.Application.Ai.IImageEmbedder? GetImageEmbedder() => null;

    public void Reset() => Resets++;
}

internal sealed class MemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public Task<string?> GetValueJsonAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetValueJsonAsync(string key, string valueJson, CancellationToken cancellationToken = default)
    {
        _values[key] = valueJson;
        return Task.CompletedTask;
    }
}

internal sealed class MemoryDecisionRepository : IPhotoDecisionRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, PhotoDecision> _stored = [];

    /// <summary>When true, saves throw (to test revert-on-failure).</summary>
    public bool FailSaves { get; set; }

    public PhotoDecision Get(Guid id)
    {
        lock (_gate)
        {
            return _stored.GetValueOrDefault(id) ?? PhotoDecision.Unset;
        }
    }

    public Task<IReadOnlyDictionary<Guid, PhotoDecision>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, PhotoDecision>>(new Dictionary<Guid, PhotoDecision>(_stored));
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, PhotoDecision Decision)> decisions, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (FailSaves)
        {
            throw new IOException("disk full (test)");
        }

        lock (_gate)
        {
            foreach (var (id, decision) in decisions)
            {
                _stored[id] = decision;
            }
        }
    }
}

internal sealed class RecordingExporter : IExportService
{
    public List<IReadOnlyList<ExportItem>> Calls { get; } = [];

    public Task<ExportSummary> CopyAsync(IReadOnlyList<ExportItem> items, string destinationFolder, IProgress<ExportProgress>? progress, CancellationToken cancellationToken)
    {
        Calls.Add(items);
        return Task.FromResult(new ExportSummary { Destination = destinationFolder, Requested = items.Count, Exported = items.Count });
    }

    public Task<ExportSummary> WriteManifestAsync(IReadOnlyList<ExportItem> items, string manifestPath, CancellationToken cancellationToken)
    {
        Calls.Add(items);
        return Task.FromResult(new ExportSummary { Destination = manifestPath, Requested = items.Count, Exported = items.Count });
    }
}
