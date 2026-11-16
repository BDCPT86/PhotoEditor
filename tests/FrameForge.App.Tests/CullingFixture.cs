using System.IO;
using FrameForge.App.Services;
using FrameForge.App.ViewModels;
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
        Settings = new SettingsService(SettingsStore, NullLogger<SettingsService>.Instance);
        Settings.SetAsync(AppSettings.ViewerAutoAdvance, autoAdvance).GetAwaiter().GetResult();

        DecisionService = new DecisionService(Decisions);
        Controller = new DecisionController(DecisionService, NullLogger<DecisionController>.Instance);
        Viewer = new ViewerViewModel(Loader, NullLogger<ViewerViewModel>.Instance);
        Compare = new CompareViewModel(Loader, NullLogger<CompareViewModel>.Instance);
        Export = new ExportViewModel(Exporter, new NullFolderPicker(), new NullSavePicker(), NullLogger<ExportViewModel>.Instance);

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
            NullLogger<LibraryViewModel>.Instance);
    }

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
