using FrameForge.Application.Photos;

namespace FrameForge.Application.Tests.Photos;

public sealed class FolderAvailabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Probe(params string[] present) : IFolderProbe
    {
        public List<string> Asked { get; } = [];

        public Func<string, bool>? Hang { get; init; }

        public bool Exists(string folderPath)
        {
            lock (Asked)
            {
                Asked.Add(folderPath);
            }

            if (Hang?.Invoke(folderPath) == true)
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }

            return present.Contains(folderPath, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AnUnpluggedDrive_MakesEveryFolderOnItOffline_AndIsAskedOnce()
    {
        var probe = new Probe(@"C:\", @"C:\Photos");

        var offline = await new FolderAvailabilityChecker(probe).CheckAsync([@"E:\Match", @"E:\Match\Day 2", @"C:\Photos"], Ct);

        Assert.Equal(2, offline.Count);
        Assert.All(offline.Values, o => Assert.Equal(FolderOffline.DriveUnavailable, o.Kind));
        Assert.Equal("Drive E: isn't connected.", offline[@"e:\match"].Reason);
        Assert.Equal(1, probe.Asked.Count(a => a.StartsWith('E')));
    }

    [Fact]
    public async Task AFolderGoneFromAConnectedDrive_IsFolderNotFound()
    {
        var offline = await new FolderAvailabilityChecker(new Probe(@"C:\", @"C:\Kept")).CheckAsync([@"C:\Kept", @"C:\Moved"], Ct);

        var only = Assert.Single(offline.Values);
        Assert.Equal(@"C:\Moved", only.Folder);
        Assert.Equal(FolderOffline.FolderNotFound, only.Kind);
    }

    [Fact]
    public async Task ADriveThatDoesntAnswer_CountsAsUnavailable_WithinTheTimeLimit()
    {
        var probe = new Probe(@"C:\") { Hang = p => p.StartsWith(@"\\nas", StringComparison.OrdinalIgnoreCase) };
        var started = DateTime.UtcNow;

        var offline = await new FolderAvailabilityChecker(probe).CheckAsync([@"\\nas\photos\2026"], Ct);

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(8));
        Assert.Equal(@"\\nas\photos can't be reached.", Assert.Single(offline.Values).Reason);
    }
}
