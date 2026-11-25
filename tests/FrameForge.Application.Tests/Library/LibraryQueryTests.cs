using FrameForge.Core.Decisions;
using FrameForge.Core.Library;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Library;

public sealed class LibraryQueryTests
{
    private sealed record Item(Photo Photo, PhotoDecision Decision);

    private static Item Make(string path, CullState state = CullState.Unset, int? rating = null, ColourLabel label = ColourLabel.None,
        DateTime? taken = null, ImportStatus status = ImportStatus.Imported, Guid? id = null) =>
        new(
            new Photo
            {
                Id = id ?? Guid.NewGuid(),
                FilePath = path,
                FileName = Path.GetFileName(path),
                FolderPath = Path.GetDirectoryName(path)!,
                CaptureLocalTime = taken,
                Status = status,
            },
            new PhotoDecision(state, rating, label));

    private static List<string> Run(IEnumerable<Item> items, LibraryFilter filter, LibrarySort sort = LibrarySort.FolderAndName) =>
        LibraryQuery.Apply(items, i => i.Photo, i => i.Decision, filter, sort).Select(i => i.Photo.FileName).ToList();

    private static readonly Item[] Sample =
    [
        Make(@"C:\B\b1.jpg", CullState.Keep, 5, ColourLabel.Red, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Unspecified)),
        Make(@"C:\A\a2.jpg", CullState.Reject, 1, taken: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)),
        Make(@"C:\A\a1.jpg", CullState.Keep, 3, ColourLabel.Green, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified)),
        Make(@"C:\A\a3.jpg", CullState.Review),
        Make(@"C:\B\b2.jpg", status: ImportStatus.Missing),
    ];

    [Fact]
    public void FilterByState()
    {
        var keep = new LibraryFilter { States = new HashSet<CullState> { CullState.Keep } };
        var unset = new LibraryFilter { States = new HashSet<CullState> { CullState.Unset } };

        Assert.Equal(["a1.jpg", "b1.jpg"], Run(Sample, keep));
        Assert.Equal(["b2.jpg"], Run(Sample, unset));
    }

    [Fact]
    public void FilterByMinimumRating_ExcludesUnrated()
    {
        Assert.Equal(["a1.jpg", "b1.jpg"], Run(Sample, new LibraryFilter { MinRating = 3 }));
        Assert.Equal(["a1.jpg", "a2.jpg", "b1.jpg"], Run(Sample, new LibraryFilter { MinRating = 1 }));
    }

    [Fact]
    public void FilterByLabel_AndProblemsOnly_AndCombined()
    {
        Assert.Equal(["a1.jpg"], Run(Sample, new LibraryFilter { Labels = new HashSet<ColourLabel> { ColourLabel.Green } }));
        Assert.Equal(["b2.jpg"], Run(Sample, new LibraryFilter { ProblemsOnly = true }));
        Assert.Equal(["b1.jpg"], Run(Sample, new LibraryFilter { States = new HashSet<CullState> { CullState.Keep }, MinRating = 4 }));
    }

    [Fact]
    public void Sorts()
    {
        Assert.Equal(["a1.jpg", "a2.jpg", "a3.jpg", "b1.jpg", "b2.jpg"], Run(Sample, LibraryFilter.All, LibrarySort.FolderAndName));
        Assert.Equal(["a2.jpg", "a1.jpg", "b1.jpg", "a3.jpg", "b2.jpg"], Run(Sample, LibraryFilter.All, LibrarySort.CaptureTime)); // no date last
        Assert.Equal(["b1.jpg", "a1.jpg", "a2.jpg", "a3.jpg", "b2.jpg"], Run(Sample, LibraryFilter.All, LibrarySort.Rating));      // unrated last
    }

    [Fact]
    public void FolderSort_MatchesComparingThePaths_IgnoringCase_WithNestedFolders()
    {
        Item[] items =
        [
            Make(@"C:\shoots\b\x.jpg"),
            Make(@"C:\Shoots\a\y.jpg"),
            Make(@"C:\Shoots\A\x.jpg"), // same folder as above, different case
            Make(@"C:\Shoots\a b\x.jpg"),
            Make(@"C:\Shoots\a\sub\x.jpg"),
            Make(@"C:\Shoots\x.jpg"),
        ];

        var expected = items
            .OrderBy(i => i.Photo.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Photo.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Photo.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(i => i.Photo.FilePath)
            .ToList();

        var actual = LibraryQuery.Apply(items, i => i.Photo, i => i.Decision, LibraryFilter.All, LibrarySort.FolderAndName).Select(i => i.Photo.FilePath).ToList();

        Assert.Equal(expected, actual);
        Assert.Equal([@"C:\Shoots\x.jpg", @"C:\Shoots\A\x.jpg", @"C:\Shoots\a\y.jpg"], actual.Take(3));
    }

    [Fact]
    public void FileNameSort_BreaksTiesByPath()
    {
        Item[] items = [Make(@"C:\Z\IMG_1.jpg"), Make(@"C:\A\IMG_1.jpg"), Make(@"C:\M\img_1.jpg")];

        var paths = LibraryQuery.Apply(items, i => i.Photo, i => i.Decision, LibraryFilter.All, LibrarySort.FileName)
            .Select(i => i.Photo.FilePath);

        Assert.Equal([@"C:\A\IMG_1.jpg", @"C:\M\img_1.jpg", @"C:\Z\IMG_1.jpg"], paths);
    }

    [Theory]
    [InlineData(LibrarySort.FolderAndName)]
    [InlineData(LibrarySort.CaptureTime)]
    [InlineData(LibrarySort.FileName)]
    [InlineData(LibrarySort.Rating)]
    public void Result_IsIndependentOfInputOrder(LibrarySort sort)
    {
        // Many equal keys: identical rating, no capture time, same names in different folders.
        var items = Enumerable.Range(0, 60)
            .Select(i => Make($@"C:\F{i % 4}\IMG_{i % 5}.jpg", rating: 2, id: new Guid(i + 1, 0, 0, new byte[8])))
            .ToList();
        var expected = Run(items, LibraryFilter.All, sort);

        var random = new Random(42);
        for (var round = 0; round < 10; round++)
        {
            var shuffled = items.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, Run(shuffled, LibraryFilter.All, sort));
        }
    }
}
