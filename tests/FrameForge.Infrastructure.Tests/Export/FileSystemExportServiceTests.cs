using System.Security.Cryptography;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Export;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Export;

public sealed class FileSystemExportServiceTests : IDisposable
{
    private readonly TempDirectory _sources = new();
    private readonly TempDirectory _destination = new();
    private readonly FileSystemExportService _service = new(NullLogger<FileSystemExportService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _sources.Dispose();
        _destination.Dispose();
    }

    private ExportItem Source(string relativePath, int bytes = 2048, byte seed = 1)
    {
        var path = Path.Combine(_sources.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = new byte[bytes];
        new Random(seed).NextBytes(content);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc));
        return new ExportItem(
            new Photo { FilePath = path, FileName = Path.GetFileName(path), FolderPath = Path.GetDirectoryName(path)! },
            new PhotoDecision(CullState.Keep));
    }

    private static (string Hash, long Size, DateTime Modified) Fingerprint(string path) =>
        (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));

    private string[] DestinationNames() =>
        Directory.EnumerateFiles(_destination.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public async Task Copies_FilesByteForByte_AndPreservesModifiedTime()
    {
        var items = new[] { Source("a.jpg", seed: 1), Source("b.jpg", seed: 2) };

        var summary = await _service.CopyAsync(items, _destination.Path, null, Ct);

        Assert.Equal(2, summary.Exported);
        Assert.Empty(summary.Failures);
        foreach (var item in items)
        {
            var copy = Path.Combine(_destination.Path, item.Photo.FileName);
            Assert.Equal(Fingerprint(item.Photo.FilePath), Fingerprint(copy));
        }
    }

    [Fact]
    public async Task NeverModifiesSources()
    {
        var items = new[] { Source("a.jpg"), Source(@"sub\b.jpg", seed: 3) };
        var before = items.Select(i => Fingerprint(i.Photo.FilePath)).ToList();

        await _service.CopyAsync(items, _destination.Path, null, Ct);
        await _service.WriteManifestAsync(items, Path.Combine(_destination.Path, "m.csv"), Ct);

        Assert.Equal(before, items.Select(i => Fingerprint(i.Photo.FilePath)));
    }

    [Fact]
    public async Task NeverOverwritesExistingDestinationFiles()
    {
        var item = Source("IMG_0001.jpg", seed: 7);
        var existing = Path.Combine(_destination.Path, "img_0001.JPG");
        File.WriteAllText(existing, "someone else's file");

        var summary = await _service.CopyAsync([item], _destination.Path, null, Ct);

        Assert.Equal("someone else's file", File.ReadAllText(existing));
        Assert.Equal(1, summary.Renamed);
        Assert.Equal(["IMG_0001 (2).jpg", "img_0001.JPG"], DestinationNames());
        Assert.Equal(Fingerprint(item.Photo.FilePath).Hash, Fingerprint(Path.Combine(_destination.Path, "IMG_0001 (2).jpg")).Hash);
    }

    [Fact]
    public async Task SameFileNameFromDifferentFolders_GetsUniqueNames()
    {
        var items = new[] { Source(@"Day1\IMG_1.jpg", seed: 1), Source(@"Day2\IMG_1.jpg", seed: 2), Source(@"Day3\IMG_1.jpg", seed: 3) };

        var summary = await _service.CopyAsync(items, _destination.Path, null, Ct);

        Assert.Equal(3, summary.Exported);
        Assert.Equal(2, summary.Renamed);
        Assert.Equal(["IMG_1 (2).jpg", "IMG_1 (3).jpg", "IMG_1.jpg"], DestinationNames());
        Assert.Equal(Fingerprint(items[1].Photo.FilePath).Hash, Fingerprint(Path.Combine(_destination.Path, "IMG_1 (2).jpg")).Hash);
    }

    [Fact]
    public async Task ExportingIntoTheSourceFolder_DoesNotTouchTheOriginal()
    {
        var item = Source("a.jpg");
        var before = Fingerprint(item.Photo.FilePath);

        var summary = await _service.CopyAsync([item], _sources.Path, null, Ct);

        Assert.Equal(before, Fingerprint(item.Photo.FilePath));
        Assert.Equal(1, summary.Renamed);
        Assert.True(File.Exists(Path.Combine(_sources.Path, "a (2).jpg")));
    }

    [Fact]
    public async Task MissingSource_IsReported_AndOthersStillCopy()
    {
        var good = Source("good.jpg");
        var gone = Source("gone.jpg");
        File.Delete(gone.Photo.FilePath);

        var summary = await _service.CopyAsync([gone, good], _destination.Path, null, Ct);

        Assert.Equal(1, summary.Exported);
        var failure = Assert.Single(summary.Failures);
        Assert.Equal(gone.Photo.FilePath, failure.SourcePath);
        Assert.Equal(["good.jpg"], DestinationNames());
    }

    [Fact]
    public async Task Cancellation_LeavesNoPartialFiles()
    {
        var items = Enumerable.Range(0, 20).Select(i => Source($"f{i:00}.jpg", bytes: 512 * 1024, seed: (byte)i)).ToList();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SyncProgress(p =>
        {
            if (p.Completed == 3)
            {
                cts.Cancel();
            }
        });

        var summary = await _service.CopyAsync(items, _destination.Path, progress, cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.InRange(summary.Exported, 3, 19);
        Assert.Empty(Directory.EnumerateFiles(_destination.Path, "*" + FileSystemExportService.PartialFileSuffix, SearchOption.AllDirectories));
        Assert.Equal(summary.Exported, DestinationNames().Length);
        foreach (var name in DestinationNames())
        {
            // Every file that exists is complete.
            Assert.Equal(512 * 1024, new FileInfo(Path.Combine(_destination.Path, name)).Length);
        }
    }

    [Fact]
    public async Task ReportsProgress_ForEveryFile()
    {
        var items = new[] { Source("a.jpg"), Source("b.jpg"), Source("c.jpg") };
        var reports = new List<ExportProgress>();

        await _service.CopyAsync(items, _destination.Path, new SyncProgress(reports.Add), Ct);

        Assert.Equal([0, 1, 2, 3], reports.Select(r => r.Completed));
        Assert.Equal(3 * 2048, reports[^1].BytesCopied);
    }

    [Fact]
    public async Task MissingDestination_Throws() =>
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _service.CopyAsync([Source("a.jpg")], Path.Combine(_destination.Path, "nope"), null, Ct));

    [Fact]
    public async Task Manifest_IsWritten_AndReplacesAnEarlierManifest()
    {
        var items = new[] { Source("a.jpg"), Source("b.jpg") };
        var path = Path.Combine(_destination.Path, "selection.csv");
        File.WriteAllText(path, "old");

        var summary = await _service.WriteManifestAsync(items, path, Ct);

        Assert.Equal(2, summary.Exported);
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Contains("a.jpg", lines[1], StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(_destination.Path, "*" + FileSystemExportService.PartialFileSuffix));
    }

    [Fact]
    public async Task Manifest_RefusesToOverwriteASourcePhoto()
    {
        var item = Source("a.jpg");
        var before = Fingerprint(item.Photo.FilePath);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.WriteManifestAsync([item], item.Photo.FilePath.ToUpperInvariant(), Ct));

        Assert.Equal(before, Fingerprint(item.Photo.FilePath));
    }

    private sealed class SyncProgress(Action<ExportProgress> onReport) : IProgress<ExportProgress>
    {
        public void Report(ExportProgress value) => onReport(value);
    }
}
