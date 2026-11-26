using FrameForge.Application.Cleanup;
using FrameForge.Infrastructure.Cleanup;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Cleanup;

/// <summary>M09 B1: the rules that keep moving to the Recycle Bin from ever deleting a file. Uses a fake Windows layer.</summary>
public sealed class RecycleBinServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RecycleBinSettings Bin = new(Enabled: true, MaxBytes: 1_000_000);

    private static RecycleDrive Drive(bool isFixed = true, bool isReady = true, RecycleBinSettings? bin = null, bool noBin = false) =>
        new(@"C:\", isFixed, isReady, noBin ? null : bin ?? Bin);

    [Theory]
    [InlineData(false, true, true, 100, RecycleRefusal.NoRecycleBinOnDrive)]  // removable or network
    [InlineData(true, false, true, 100, RecycleRefusal.DriveUnavailable)]
    [InlineData(true, true, false, 100, RecycleRefusal.RecycleBinOff)]
    [InlineData(true, true, true, 1_000_000, RecycleRefusal.TooLarge)]
    [InlineData(true, true, true, 999_999, RecycleRefusal.None)]
    public void Refusal_FollowsTheDrive(bool isFixed, bool isReady, bool enabled, long size, RecycleRefusal expected) =>
        Assert.Equal(expected, RecycleBinService.Refusal(Drive(isFixed, isReady, Bin with { Enabled = enabled }), size));

    [Fact]
    public void NoDrive_OrNoRecycleBinSettings_IsRefused()
    {
        Assert.Equal(RecycleRefusal.NoRecycleBinOnDrive, RecycleBinService.Refusal(null, 1));
        Assert.Equal(RecycleRefusal.RecycleBinUnknown, RecycleBinService.Refusal(Drive(noBin: true), 1));
    }

    [Fact]
    public async Task Check_GivesSizes_AndReasons_WithoutMovingAnything()
    {
        var platform = new FakePlatform();
        platform.Files[@"C:\Shoot\a.jpg"] = 10;
        platform.Files[@"E:\Card\b.jpg"] = 20;
        platform.Drives['E'] = Drive(isFixed: false) with { Root = @"E:\" };
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var checks = await service.CheckAsync([@"C:\Shoot\a.jpg", @"E:\Card\b.jpg", @"C:\Shoot\gone.jpg"], Ct);

        Assert.Equal([RecycleRefusal.None, RecycleRefusal.NoRecycleBinOnDrive, RecycleRefusal.Missing], checks.Select(c => c.Refusal));
        Assert.Equal([10L, 20L, 0L], checks.Select(c => c.Size));
        Assert.Empty(platform.Recycled);
    }

    [Fact]
    public async Task Move_RecyclesWhatPasses_SkipsTheRest_AndReportsEachFile()
    {
        var platform = new FakePlatform();
        platform.Files[@"C:\Shoot\a.jpg"] = 10;
        platform.Files[@"C:\Shoot\b.cr3"] = 20;
        platform.Files[@"E:\Card\c.jpg"] = 30;
        platform.Drives['E'] = Drive(isFixed: false) with { Root = @"E:\" };
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);
        var reports = new List<RecycleProgress>();

        var summary = await service.MoveAsync([@"C:\Shoot\a.jpg", @"E:\Card\c.jpg", @"C:\Shoot\b.cr3"], new SyncProgress(reports), Ct);

        Assert.Equal([RecycleOutcome.Moved, RecycleOutcome.Skipped, RecycleOutcome.Moved], summary.Files.Select(f => f.Outcome));
        Assert.Equal(RecycleRefusal.NoRecycleBinOnDrive, summary.Files[1].Refusal);
        Assert.Equal([@"C:\Shoot\a.jpg", @"C:\Shoot\b.cr3"], platform.Recycled);
        Assert.True(platform.Files.ContainsKey(@"E:\Card\c.jpg")); // never touched
        Assert.Equal((2, 1, 0, false), (summary.Moved, summary.Skipped, summary.Failed, summary.StoppedForSafety));
        Assert.Equal(3, reports[^1].Done);
    }

    [Fact]
    public async Task AFileWindowsLeavesInPlace_IsAFailure_AndMovingGoesOn()
    {
        var platform = new FakePlatform();
        platform.Files[@"C:\Shoot\locked.jpg"] = 10;
        platform.Files[@"C:\Shoot\b.jpg"] = 10;
        platform.Behaviour[@"C:\Shoot\locked.jpg"] = FakePlatform.Outcome.LeftInPlace;
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var summary = await service.MoveAsync([@"C:\Shoot\locked.jpg", @"C:\Shoot\b.jpg"], null, Ct);

        Assert.Equal([RecycleOutcome.Failed, RecycleOutcome.Moved], summary.Files.Select(f => f.Outcome));
        Assert.Contains("in use", summary.Files[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileGoneWithoutReachingTheRecycleBin_StopsEverything()
    {
        var platform = new FakePlatform();
        foreach (var name in new[] { "a", "b", "c" })
        {
            platform.Files[$@"C:\Shoot\{name}.jpg"] = 10;
        }

        platform.Behaviour[@"C:\Shoot\a.jpg"] = FakePlatform.Outcome.GoneButNotInBin;
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var summary = await service.MoveAsync([@"C:\Shoot\a.jpg", @"C:\Shoot\b.jpg", @"C:\Shoot\c.jpg"], null, Ct);

        Assert.Equal([RecycleOutcome.NotConfirmed, RecycleOutcome.NotTried, RecycleOutcome.NotTried], summary.Files.Select(f => f.Outcome));
        Assert.True(summary.StoppedForSafety);
        Assert.Equal([@"C:\Shoot\a.jpg"], platform.Recycled); // b and c were never handed to Windows
    }

    [Fact]
    public async Task EachFile_IsCheckedAgainJustBeforeItsMove()
    {
        var platform = new FakePlatform();
        platform.Files[@"C:\Shoot\a.jpg"] = 10;
        platform.Files[@"C:\Shoot\b.jpg"] = 10;
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);
        await service.CheckAsync([@"C:\Shoot\a.jpg", @"C:\Shoot\b.jpg"], Ct);

        // Between the confirmation and the move, the Recycle Bin is turned off.
        platform.Drives['C'] = Drive(bin: Bin with { Enabled = false });
        var summary = await service.MoveAsync([@"C:\Shoot\a.jpg", @"C:\Shoot\b.jpg"], null, Ct);

        Assert.All(summary.Files, f => Assert.Equal(RecycleRefusal.RecycleBinOff, f.Refusal));
        Assert.Empty(platform.Recycled);
    }

    [Fact]
    public async Task AnUnreadableRecycleBin_MeansTheFileIsSkipped()
    {
        var platform = new FakePlatform { CountReadable = false };
        platform.Files[@"C:\Shoot\a.jpg"] = 10;
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var summary = await service.MoveAsync([@"C:\Shoot\a.jpg"], null, Ct);

        Assert.Equal(RecycleOutcome.Skipped, Assert.Single(summary.Files).Outcome);
        Assert.Empty(platform.Recycled);
    }

    [Fact]
    public async Task Cancelling_LeavesTheRestUntried()
    {
        var platform = new FakePlatform();
        platform.Files[@"C:\Shoot\a.jpg"] = 10;
        platform.Files[@"C:\Shoot\b.jpg"] = 10;
        using var cts = new CancellationTokenSource();
        platform.AfterRecycle = cts.Cancel;
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var summary = await service.MoveAsync([@"C:\Shoot\a.jpg", @"C:\Shoot\b.jpg"], null, cts.Token);

        Assert.Equal([RecycleOutcome.Moved, RecycleOutcome.NotTried], summary.Files.Select(f => f.Outcome));
        Assert.True(summary.Cancelled);
    }

    private sealed class SyncProgress(List<RecycleProgress> reports) : IProgress<RecycleProgress>
    {
        public void Report(RecycleProgress value) => reports.Add(value);
    }

    /// <summary>Files and drives in memory; "recycling" removes the file and adds an item to the bin.</summary>
    private sealed class FakePlatform : IRecycleBinPlatform
    {
        public enum Outcome { Recycled, LeftInPlace, GoneButNotInBin }

        public Dictionary<string, long> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<char, RecycleDrive> Drives { get; } = new() { ['C'] = Drive() };

        public Dictionary<string, Outcome> Behaviour { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Recycled { get; } = [];

        public bool CountReadable { get; init; } = true;

        public Action? AfterRecycle { get; set; }

        private long _items = 5;

        public RecycleDrive? GetDrive(string path) => Drives.GetValueOrDefault(char.ToUpperInvariant(path[0]));

        public long? FileSize(string path) => Files.TryGetValue(path, out var size) ? size : null;

        public long? ItemsInRecycleBin(string root) => CountReadable ? _items : null;

        public ShellRecycleResult Recycle(string path)
        {
            Recycled.Add(path);
            var result = Behaviour.GetValueOrDefault(path, Outcome.Recycled) switch
            {
                Outcome.LeftInPlace => new ShellRecycleResult(false, "The file is in use."),
                Outcome.GoneButNotInBin => Gone(path, intoBin: false),
                _ => Gone(path, intoBin: true),
            };
            AfterRecycle?.Invoke();
            return result;
        }

        private ShellRecycleResult Gone(string path, bool intoBin)
        {
            Files.Remove(path);
            _items += intoBin ? 1 : 0;
            return new ShellRecycleResult(true, null);
        }
    }
}
