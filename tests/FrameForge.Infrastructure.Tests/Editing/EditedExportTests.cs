using System.Security.Cryptography;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Export;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Editing;

/// <summary>Edited export with the stub decoder (16×16 grey) and stub encoder; WIC encoding is tested in Imaging.Tests.</summary>
public sealed class EditedExportTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _source;
    private readonly string _destination;

    public EditedExportTests()
    {
        _source = Directory.CreateDirectory(Path.Combine(_temp.Path, "source")).FullName;
        _destination = Directory.CreateDirectory(Path.Combine(_temp.Path, "out")).FullName;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private Photo PhotoFile(string name)
    {
        var path = Path.Combine(_source, name);
        File.WriteAllBytes(path, [1, 2, 3, (byte)name.Length]);
        return new Photo { Id = Guid.NewGuid(), FilePath = path, FileName = name, FolderPath = _source, Status = ImportStatus.Imported, Width = 16, Height = 16 };
    }

    private async Task<(ExportSummary Summary, List<ExportProgress> Progress)> ExportAsync(IReadOnlyList<EditedExportItem> items, EditedExportOptions? options = null, CancellationToken? token = null)
    {
        await using var services = TestServices.Build(new AppDataPaths(Path.Combine(_temp.Path, "data")));
        var progress = new List<ExportProgress>();
        var summary = await services.GetRequiredService<IEditedExportService>().ExportAsync(
            items, _destination, options ?? EditedExportOptions.Default, new SyncProgress<ExportProgress>(progress.Add), token ?? Ct);
        return (summary, progress);
    }

    [Fact]
    public async Task Export_WritesJpegNamedFiles_LeavesOriginalsByteIdentical()
    {
        var a = PhotoFile("a.JPG");
        var b = PhotoFile("b.jpeg");
        var before = new[] { a, b }.Select(p => SHA256.HashData(File.ReadAllBytes(p.FilePath))).ToList();

        var (summary, progress) = await ExportAsync([new(a, EditRecipe.Neutral with { Exposure = 1 }), new(b, EditRecipe.Neutral with { Crop = new CropRect(0, 0, 0.5, 0.5) })]);

        Assert.Equal(2, summary.Exported);
        Assert.Empty(summary.Failures);
        Assert.True(File.Exists(Path.Combine(_destination, "a.jpg")));
        Assert.True(File.Exists(Path.Combine(_destination, "b.jpg")));
        Assert.Equal(before, new[] { a, b }.Select(p => SHA256.HashData(File.ReadAllBytes(p.FilePath))).ToList());
        Assert.Equal(2, progress[^1].Completed);
        Assert.Empty(Directory.GetFiles(_destination, "*" + FileSystemExportService.PartialFileSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Export_RendersTheRecipe()
    {
        var a = PhotoFile("a.jpg");

        await ExportAsync([new(a, EditRecipe.Neutral with { Crop = new CropRect(0, 0, 0.5, 0.5) })]);

        // Stub encoder writes "STUBJPEG" + raw BGRA: an 8×8 crop of the 16×16 source is 256 bytes.
        Assert.Equal(8 + (8 * 8 * 4), new FileInfo(Path.Combine(_destination, "a.jpg")).Length);
    }

    [Fact]
    public async Task Export_NeverOverwrites()
    {
        var a = PhotoFile("a.jpg");
        await File.WriteAllTextAsync(Path.Combine(_destination, "a.jpg"), "existing", Ct);

        var (summary, _) = await ExportAsync([new(a, EditRecipe.Neutral)]);

        Assert.Equal("existing", await File.ReadAllTextAsync(Path.Combine(_destination, "a.jpg"), Ct));
        Assert.True(File.Exists(Path.Combine(_destination, "a (2).jpg")));
        Assert.Equal(1, summary.Renamed);
    }

    [Fact]
    public async Task MissingSource_IsReported_OthersContinue()
    {
        var gone = PhotoFile("gone.jpg");
        File.Delete(gone.FilePath);
        var ok = PhotoFile("ok.jpg");

        var (summary, _) = await ExportAsync([new(gone, EditRecipe.Neutral), new(ok, EditRecipe.Neutral)]);

        Assert.Equal(1, summary.Exported);
        Assert.Equal(gone.FilePath, Assert.Single(summary.Failures).SourcePath);
    }

    [Fact]
    public async Task Cancellation_StopsAndLeavesNoPartialFiles()
    {
        var photos = Enumerable.Range(0, 5).Select(i => PhotoFile($"p{i}.jpg")).ToList();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new List<ExportProgress>();
        await using var services = TestServices.Build(new AppDataPaths(Path.Combine(_temp.Path, "data")));

        var summary = await services.GetRequiredService<IEditedExportService>().ExportAsync(
            photos.Select(p => new EditedExportItem(p, EditRecipe.Neutral)).ToList(),
            _destination,
            EditedExportOptions.Default,
            new SyncProgress<ExportProgress>(p =>
            {
                progress.Add(p);
                if (p.Completed == 2)
                {
                    cts.Cancel();
                }
            }),
            cts.Token);

        Assert.True(summary.WasCancelled);
        Assert.Equal(2, summary.Exported);
        Assert.Empty(Directory.GetFiles(_destination, "*" + FileSystemExportService.PartialFileSuffix, SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(40, null)]
    [InlineData(92, 10)]
    public async Task InvalidOptions_AreRejected(int quality, int? maxSide)
    {
        var a = PhotoFile("a.jpg");
        await Assert.ThrowsAsync<ArgumentException>(() => ExportAsync([new(a, EditRecipe.Neutral)], new EditedExportOptions { Quality = quality, MaxLongSide = maxSide }));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
