using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Photos;

public sealed class PhotoImportServiceTests
{
    private const string Root = @"C:\Photos\Shoot";

    private readonly FakeEnumerator _files = new();
    private readonly FakeMetadataReader _reader = new();
    private readonly InMemoryPhotoRepository _repository = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PhotoImportService CreateService(int maxParallelism = 4, int batchSize = 50) =>
        new(_files, _reader, _repository,
            new PhotoImportOptions { MaxParallelism = maxParallelism, BatchSize = batchSize, ProgressInterval = TimeSpan.Zero },
            TimeProvider.System, NullLogger<PhotoImportService>.Instance);

    private void AddFiles(int count, string folder = Root)
    {
        for (var i = 0; i < count; i++)
        {
            _files.AddFile($@"{folder}\IMG_{i:0000}.jpg");
        }
    }

    [Fact]
    public async Task FirstImport_AddsEveryFileWithMetadata()
    {
        AddFiles(120);

        var summary = await CreateService().ImportFolderAsync(Root, includeSubfolders: true, progress: null, Ct);

        Assert.Equal(120, summary.Discovered);
        Assert.Equal(120, summary.Added);
        Assert.Equal(0, summary.Failed);
        Assert.False(summary.WasCancelled);
        Assert.Equal(120, _repository.All.Count);
        Assert.All(_repository.All, p =>
        {
            Assert.Equal(ImportStatus.Imported, p.Status);
            Assert.Equal("Test Camera", p.CameraModel);
            Assert.Equal(Root, p.FolderPath);
        });
    }

    [Fact]
    public async Task ReImport_OfUnchangedFolder_CreatesNoDuplicatesAndReadsNothing()
    {
        AddFiles(30);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);
        var idsBefore = _repository.All.Select(p => p.Id).ToHashSet();
        var readsBefore = _reader.ReadCount;

        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(30, summary.Unchanged);
        Assert.Equal(0, summary.Added);
        Assert.Equal(0, summary.Updated);
        Assert.Equal(readsBefore, _reader.ReadCount);
        Assert.Equal(30, _repository.All.Count);
        Assert.True(idsBefore.SetEquals(_repository.All.Select(p => p.Id)));
    }

    [Fact]
    public async Task ReImport_WithDifferentPathCasing_DoesNotCreateDuplicates()
    {
        _files.AddFile($@"{Root}\IMG_0001.jpg");
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);

        var summary = await service.ImportFolderAsync(Root.ToUpperInvariant(), true, null, Ct);

        Assert.Single(_repository.All);
        Assert.Equal(1, summary.Unchanged);
    }

    [Fact]
    public async Task ChangedFile_IsReRead_AndKeepsItsId()
    {
        var path = $@"{Root}\IMG_0001.jpg";
        _files.AddFile(path, size: 1000);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);
        var originalId = _repository.Get(path).Id;

        _files.AddFile(path, size: 2000, modifiedUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(1, summary.Updated);
        Assert.Equal(2, _reader.ReadCount);
        Assert.Equal(originalId, _repository.Get(path).Id);
        Assert.Equal(2000, _repository.Get(path).FileSizeBytes);
    }

    [Fact]
    public async Task CorruptFile_IsRecordedAsFailed_WithoutStoppingTheImport()
    {
        AddFiles(10);
        var corrupt = $@"{Root}\IMG_0003.jpg";
        _reader.CorruptFiles.Add(corrupt);

        var summary = await CreateService().ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(9, summary.Added);
        Assert.Equal(1, summary.Failed);
        var failed = _repository.Get(corrupt);
        Assert.Equal(ImportStatus.Failed, failed.Status);
        Assert.Contains("corrupt", failed.LastError, StringComparison.Ordinal);
        Assert.Equal(9, _repository.All.Count(p => p.Status == ImportStatus.Imported));
    }

    [Fact]
    public async Task FailedFile_IsRetriedOnNextImport_AndRecoversWhenReadable()
    {
        var path = $@"{Root}\IMG_0001.jpg";
        _files.AddFile(path);
        _reader.CorruptFiles.Add(path);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);

        _reader.CorruptFiles.Clear();
        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(1, summary.Updated);
        Assert.Equal(ImportStatus.Imported, _repository.Get(path).Status);
        Assert.Null(_repository.Get(path).LastError);
    }

    [Fact]
    public async Task Cancellation_KeepsWorkDone_AndNextImportResumesWithoutRereading()
    {
        AddFiles(20);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _reader.AfterRead = count =>
        {
            if (count == 7)
            {
                cts.Cancel();
            }
        };

        // Sequential, small batches: deterministic cancellation point.
        var service = CreateService(maxParallelism: 1, batchSize: 5);
        var cancelled = await service.ImportFolderAsync(Root, true, null, cts.Token);

        Assert.True(cancelled.WasCancelled);
        var persisted = _repository.All.Count;
        Assert.Equal(_reader.ReadCount, persisted); // everything read was saved
        Assert.InRange(persisted, 7, 19);
        Assert.Equal(persisted, cancelled.Added);

        _reader.AfterRead = null;
        var readsBeforeResume = _reader.ReadCount;
        var resumed = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.False(resumed.WasCancelled);
        Assert.Equal(persisted, resumed.Unchanged);
        Assert.Equal(20 - persisted, resumed.Added);
        Assert.Equal(20 - persisted, _reader.ReadCount - readsBeforeResume);
        Assert.Equal(20, _repository.All.Count);
    }

    [Fact]
    public async Task Cancellation_DoesNotMarkUnscannedFilesMissing()
    {
        AddFiles(10);
        var service = CreateService(maxParallelism: 1, batchSize: 2);
        await service.ImportFolderAsync(Root, true, null, Ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _files.RemoveFile($@"{Root}\IMG_0000.jpg");
        _files.AddFile($@"{Root}\NEW_0001.jpg");
        _files.AddFile($@"{Root}\NEW_0002.jpg");
        _reader.AfterRead = _ => cts.Cancel();

        var summary = await service.ImportFolderAsync(Root, true, null, cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(0, summary.MarkedMissing);
        Assert.Equal(ImportStatus.Imported, _repository.Get($@"{Root}\IMG_0000.jpg").Status);
    }

    [Fact]
    public async Task DeletedFile_IsMarkedMissing_AndRecoversWhenItReturns()
    {
        AddFiles(3);
        var path = $@"{Root}\IMG_0001.jpg";
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);

        _files.RemoveFile(path);
        var afterDelete = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(1, afterDelete.MarkedMissing);
        Assert.Equal(ImportStatus.Missing, _repository.Get(path).Status);
        Assert.Equal(3, _repository.All.Count); // never deleted

        _files.AddFile(path);
        var afterRestore = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(1, afterRestore.Updated);
        Assert.Equal(ImportStatus.Imported, _repository.Get(path).Status);
    }

    [Fact]
    public async Task AFolderThatLooksEmpty_DoesNotMarkItsPhotosMissing()
    {
        AddFiles(3);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);
        foreach (var photo in _repository.All)
        {
            _files.RemoveFile(photo.FilePath);
        }

        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(0, summary.MarkedMissing);
        Assert.Equal(3, summary.MissingCheckSkipped);
        Assert.All(_repository.All, p => Assert.Equal(ImportStatus.Imported, p.Status));
    }

    [Fact]
    public async Task OneFileGone_IsStillMarkedMissing_AndNothingIsSkipped()
    {
        AddFiles(3);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);
        _files.RemoveFile($@"{Root}\IMG_0002.jpg");

        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(1, summary.MarkedMissing);
        Assert.Equal(0, summary.MissingCheckSkipped);
    }

    [Fact]
    public async Task MissingDetection_IsScopedToTheImportedFolder()
    {
        AddFiles(2, Root);
        AddFiles(2, @"C:\Photos\Other");
        AddFiles(2, $@"{Root}\Sub");
        var service = CreateService();
        await service.ImportFolderAsync(@"C:\Photos", true, null, Ct);

        // Import only the root, non-recursively: other folders and sub-folders are out of scope.
        var summary = await service.ImportFolderAsync(Root, includeSubfolders: false, null, Ct);

        Assert.Equal(0, summary.MarkedMissing);
        Assert.All(_repository.All, p => Assert.Equal(ImportStatus.Imported, p.Status));
    }

    [Fact]
    public async Task PhotosInUnreadableSubfolder_AreNotMarkedMissing()
    {
        var locked = $@"{Root}\Locked";
        AddFiles(2, Root);
        AddFiles(3, locked);
        var service = CreateService();
        await service.ImportFolderAsync(Root, true, null, Ct);

        _files.UnreadableFolders.Add(locked);
        var summary = await service.ImportFolderAsync(Root, true, null, Ct);

        Assert.Equal(0, summary.MarkedMissing);
        Assert.Equal([locked], summary.SkippedFolders);
        Assert.All(_repository.All, p => Assert.Equal(ImportStatus.Imported, p.Status));
    }

    [Fact]
    public async Task NonRecursiveImport_IgnoresSubfolders()
    {
        AddFiles(2, Root);
        AddFiles(5, $@"{Root}\Sub");

        var summary = await CreateService().ImportFolderAsync(Root, includeSubfolders: false, null, Ct);

        Assert.Equal(2, summary.Discovered);
        Assert.Equal(2, _repository.All.Count);
    }

    [Fact]
    public async Task Progress_EndsWithAllFilesProcessed()
    {
        AddFiles(25);
        _reader.CorruptFiles.Add($@"{Root}\IMG_0002.jpg");
        var progress = new RecordingProgress<ImportProgress>();

        await CreateService(batchSize: 10).ImportFolderAsync(Root, true, progress, Ct);

        var reports = progress.Reports;
        Assert.Equal(ImportPhase.Scanning, reports[0].Phase);
        var lastReading = reports.Last(r => r.Phase == ImportPhase.Reading);
        Assert.Equal(25, lastReading.Total);
        Assert.Equal(25, lastReading.Processed);
        Assert.Equal(1, lastReading.Failed);
        Assert.Equal(ImportPhase.Finishing, reports[^1].Phase);
    }

    [Fact]
    public async Task UnexpectedReaderError_Propagates_AfterSavingCompletedFiles()
    {
        AddFiles(4);
        var service = new PhotoImportService(
            _files,
            new ThrowingReader(failOnCall: 3),
            _repository,
            new PhotoImportOptions { MaxParallelism = 1, BatchSize = 10, ProgressInterval = TimeSpan.Zero },
            TimeProvider.System,
            NullLogger<PhotoImportService>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportFolderAsync(Root, true, null, Ct));

        Assert.Equal(2, _repository.All.Count);
    }

    private sealed class ThrowingReader(int failOnCall) : IPhotoMetadataReader
    {
        private int _calls;

        public PhotoMetadata Read(string filePath) =>
            ++_calls == failOnCall ? throw new InvalidOperationException("bug") : PhotoMetadata.Empty;
    }
}
