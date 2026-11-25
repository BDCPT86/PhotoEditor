using FrameForge.App.ViewModels;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

/// <summary>M07 phase C: photos on a drive or folder that can't be reached show as Offline, and Locate folder… previews a move.</summary>
public sealed class OfflineFolderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUnpluggedDrive_ShowsItsPhotosOffline_WithTheReason_AndCountsThemAsProblems()
    {
        using var f = new CullingFixture(photoCount: 3);
        f.FolderProbe.Gone.Add(@"C:\");

        await f.LoadAsync();

        Assert.All(f.Library.Photos, t => Assert.Equal("Offline", t.StatusText));
        Assert.Contains("Drive C: isn't connected.", f.Tile(0).ToolTipText, StringComparison.Ordinal);
        Assert.Contains("Drive C: isn't connected. 3 photos are offline", f.Library.Locate.BannerText, StringComparison.Ordinal);
        Assert.Equal(@"C:\Shoot", f.Library.Locate.BannerFolder);
        Assert.Equal(3, f.Library.Filter.Scopes.Single(s => s.Scope == LibraryScope.Problems).Count);
        Assert.True(f.Library.Filter.Folders.Single(o => o.Path == @"C:\Shoot").IsOffline);
    }

    [Fact]
    public async Task TheProblemsView_ListsOfflinePhotos()
    {
        using var f = new CullingFixture(photoCount: 2);
        f.FolderProbe.Gone.Add(@"C:\Shoot");
        await f.LoadAsync();

        f.Library.Filter.SelectedScope = f.Library.Filter.Scopes.Single(s => s.Scope == LibraryScope.Problems);

        Assert.Equal(2, f.Library.Photos.Count);
        Assert.StartsWith("This folder isn't there any more.", f.Tile(0).OfflineReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithEveryFolderThere_NothingIsOffline_AndThereIsNoBanner()
    {
        using var f = new CullingFixture(photoCount: 2);

        await f.LoadAsync();

        Assert.All(f.Library.Photos, t => Assert.Null(t.OfflineReason));
        Assert.False(f.Library.Locate.HasBanner);
    }

    [Fact]
    public async Task Locate_ShowsWhatWasFound_AndMovesOnlyMatchedPhotos()
    {
        var plan = new RelocationPlan(@"E:\Match", @"D:\Match",
        [
            new RelocationItem(Guid.NewGuid(), @"E:\Match\1.jpg", @"D:\Match\1.jpg", RelocationOutcome.Found),
            new RelocationItem(Guid.NewGuid(), @"E:\Match\2.jpg", @"D:\Match\2.jpg", RelocationOutcome.Found),
            new RelocationItem(Guid.NewGuid(), @"E:\Match\3.jpg", @"D:\Match\3.jpg", RelocationOutcome.NotFound),
        ], []);
        var service = new ScriptedRelocation(plan);
        using var locate = new LocateFolderViewModel(service, new FolderAvailabilityChecker(new CullingFixture.TestFolderProbe()),
            new CullingFixture.ChoosingFolderPicker { Next = @"D:\Match" }, NullLogger<LocateFolderViewModel>.Instance);
        var relocated = -1;
        locate.Relocated += (_, n) => relocated = n;

        await locate.BeginCommand.ExecuteAsync(@"E:\Match");

        Assert.True(locate.IsOpen);
        Assert.Equal(["2 found", "1 not in the new folder"], locate.Lines.Select(l => l.Text));
        Assert.Equal("Move 2 photos", locate.ApplyText);

        await locate.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(2, relocated);
        Assert.False(locate.IsOpen);
        Assert.Same(plan, service.Applied);
    }

    [Fact]
    public async Task Locate_CancelledPicker_ClosesWithoutChanges()
    {
        var service = new ScriptedRelocation(new RelocationPlan("a", "b", [], []));
        using var locate = new LocateFolderViewModel(service, new FolderAvailabilityChecker(new CullingFixture.TestFolderProbe()),
            new CullingFixture.ChoosingFolderPicker { Next = null }, NullLogger<LocateFolderViewModel>.Instance);

        await locate.BeginCommand.ExecuteAsync(@"E:\Match");

        Assert.False(locate.IsOpen);
        Assert.Null(service.Applied);
    }

    private sealed class ScriptedRelocation(RelocationPlan plan) : IFolderRelocationService
    {
        public RelocationPlan? Applied { get; private set; }

        public Task<RelocationPlan> PreviewAsync(string oldFolder, string newFolder, CancellationToken cancellationToken) => Task.FromResult(plan);

        public Task<int> ApplyAsync(RelocationPlan p, CancellationToken cancellationToken)
        {
            Applied = p;
            return Task.FromResult(p.Movable);
        }
    }
}
