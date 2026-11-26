using FrameForge.Infrastructure.Photos;
using FrameForge.Infrastructure.Tests.TestSupport;

namespace FrameForge.Infrastructure.Tests.Photos;

public sealed class FileSystemPhotoEnumeratorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileSystemPhotoEnumerator _enumerator = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private string Touch(string relativePath, int bytes = 10)
    {
        var path = Path.Combine(_temp.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void FindsJpegExtensionsInAnyCase_AndIgnoresOtherFiles()
    {
        Touch("a.jpg");
        Touch("b.JPG");
        Touch("c.jpeg");
        Touch("d.JpEg");
        Touch("e.png");
        Touch("f.jpg.txt");
        Touch("noextension");

        var scan = _enumerator.Scan(_temp.Path, includeSubfolders: false, Ct);

        Assert.Equal(["a.jpg", "b.JPG", "c.jpeg", "d.JpEg"], scan.Files.Select(f => Path.GetFileName(f.FilePath)));
        Assert.Empty(scan.SkippedFolders);
    }

    [Fact]
    public void OtherPhotoFormats_AreCountedByExtension_OtherFilesAreNot()
    {
        Touch("a.CR3");
        Touch("b.nef");
        Touch("c.NEF");
        Touch("d.heic");
        Touch(@"sub\e.dng");
        Touch("a.xmp");
        Touch("clip.mp4");
        Touch("notes.txt");

        var flat = _enumerator.Scan(_temp.Path, includeSubfolders: false, Ct);
        var recursive = _enumerator.Scan(_temp.Path, includeSubfolders: true, Ct);

        Assert.Equal(["a.CR3"], flat.Files.Select(f => Path.GetFileName(f.FilePath)));
        Assert.Equal(new Dictionary<string, int> { [".NEF"] = 2, [".HEIC"] = 1 }, flat.UnsupportedFiles);
        Assert.Equal(1, recursive.UnsupportedFiles[".DNG"]);
    }

    [Fact]
    public void Recursive_IncludesNestedFolders_NonRecursive_DoesNot()
    {
        Touch("top.jpg");
        Touch(@"2026\01\nested.jpg");
        Touch(@"2026\deeper\more\deepest.jpg");

        var recursive = _enumerator.Scan(_temp.Path, includeSubfolders: true, Ct);
        var flat = _enumerator.Scan(_temp.Path, includeSubfolders: false, Ct);

        Assert.Equal(3, recursive.Files.Count);
        Assert.Single(flat.Files);
    }

    [Fact]
    public void RecordsSizeAndUtcModifiedTime()
    {
        var path = Touch("a.jpg", bytes: 1234);
        var modified = new DateTime(2025, 7, 1, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);

        var file = Assert.Single(_enumerator.Scan(_temp.Path, false, Ct).Files);

        Assert.Equal(path, file.FilePath);
        Assert.Equal(1234, file.FileSizeBytes);
        Assert.Equal(modified, file.LastModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, file.LastModifiedUtc.Kind);
    }

    [Fact]
    public void MissingRootFolder_Throws() =>
        Assert.Throws<DirectoryNotFoundException>(() => _enumerator.Scan(Path.Combine(_temp.Path, "nope"), true, Ct));

    [Fact]
    public void HonoursCancellation()
    {
        Touch("a.jpg");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _enumerator.Scan(_temp.Path, true, cts.Token));
    }

    [Fact]
    public void EmptyFolder_ReturnsNoFiles() =>
        Assert.Empty(_enumerator.Scan(_temp.Path, true, Ct).Files);
}
