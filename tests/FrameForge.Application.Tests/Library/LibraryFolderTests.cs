using FrameForge.Core.Decisions;
using FrameForge.Core.Library;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Library;

/// <summary>The Folder filter: which photos a folder covers, and the list of folders to choose from.</summary>
public sealed class LibraryFolderTests
{
    [Theory]
    [InlineData(@"C:\Shoot", @"C:\Shoot", true)]
    [InlineData(@"C:\Shoot\Day 2", @"C:\Shoot", true)]   // subfolders are included
    [InlineData(@"c:\shoot", @"C:\Shoot\", true)]         // case and a trailing slash do not matter
    [InlineData(@"C:\Shoot2", @"C:\Shoot", false)]       // a longer name is a different folder
    [InlineData(@"C:\Other", @"C:\Shoot", false)]
    public void IsInFolder(string photoFolder, string folder, bool expected) =>
        Assert.Equal(expected, LibraryFilter.IsInFolder(photoFolder, folder));

    [Fact]
    public void FolderFilter_KeepsOnlyThatFoldersPhotos()
    {
        Photo P(string folder, string name) => new() { Id = Guid.NewGuid(), FilePath = Path.Combine(folder, name), FileName = name, FolderPath = folder, Status = ImportStatus.Imported };
        var photos = new[] { P(@"C:\Poen\Randoms", "a.jpg"), P(@"C:\Club\Match 1", "b.jpg"), P(@"C:\Poen\Randoms\Extra", "c.jpg") };

        var shown = LibraryQuery.Apply(photos, p => p, _ => PhotoDecision.Unset, new LibraryFilter { Folder = @"C:\Poen\Randoms" }, LibrarySort.FolderAndName);

        Assert.Equal(["a.jpg", "c.jpg"], shown.Select(p => p.FileName));
    }

    [Fact]
    public void Build_NestsSubfolders_UnderTheirNearestListedParent()
    {
        var folders = LibraryFolders.Build(
        [
            @"C:\Shoot", @"C:\Shoot\Day 1", @"C:\Shoot\2026\Day 2", @"C:\Other",
        ]);

        Assert.Equal(
            [(@"C:\Other", 0, "Other"), (@"C:\Shoot", 0, "Shoot"), (@"C:\Shoot\2026\Day 2", 1, @"2026\Day 2"), (@"C:\Shoot\Day 1", 1, "Day 1")],
            folders.Select(f => (f.Path, f.Depth, f.Name)));
    }

    [Fact]
    public void ImportedSince_KeepsOnlyPhotosFirstImportedFromThatTime()
    {
        var before = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var after = before.AddDays(1);
        Photo P(string name, DateTime imported) => new() { Id = Guid.NewGuid(), FilePath = $@"C:\S\{name}", FileName = name, FolderPath = @"C:\S", ImportedUtc = imported };
        var photos = new[] { P("old.jpg", before), P("new.jpg", after) };

        var shown = LibraryQuery.Apply(photos, p => p, _ => PhotoDecision.Unset, new LibraryFilter { ImportedSinceUtc = after }, LibrarySort.FileName);

        Assert.Equal(["new.jpg"], shown.Select(p => p.FileName));
    }

    [Fact]
    public void Build_ListsEachFolderOnce_WithItsPhotosAndItsSubfolders()
    {
        var folders = LibraryFolders.Build(
        [
            @"C:\Club\Match 1", @"C:\Club\Match 1",
            @"C:\Poen\Randoms", @"c:\poen\randoms",
            @"C:\Poen\Randoms\Extra",
        ]);

        Assert.Equal([@"C:\Club\Match 1", @"C:\Poen\Randoms", @"C:\Poen\Randoms\Extra"], folders.Select(f => f.Path));
        Assert.Equal(["Match 1", "Randoms", "Extra"], folders.Select(f => f.Label));
        Assert.Equal([2, 3, 1], folders.Select(f => f.Count)); // Randoms counts its subfolder too
    }

    [Fact]
    public void Build_SameFolderName_ShowsEnoughOfThePathToTellThemApart()
    {
        var folders = LibraryFolders.Build([@"D:\2025\Randoms", @"D:\2026\Randoms", @"D:\2026\Cup final"]);

        Assert.Equal([@"2025\Randoms", "Cup final", @"2026\Randoms"], folders.Select(f => f.Label));
    }

    [Fact]
    public void Build_NoPhotos_NoFolders() => Assert.Empty(LibraryFolders.Build([]));
}
