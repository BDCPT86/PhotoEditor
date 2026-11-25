using FrameForge.Infrastructure.Diagnostics;
using FrameForge.Infrastructure.Tests.TestSupport;

namespace FrameForge.Infrastructure.Tests.Diagnostics;

public sealed class FileCrashReportsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly FileCrashReports _reports;

    public FileCrashReportsTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
        _reports = new FileCrashReports(_paths, _time);
    }

    public void Dispose() => _temp.Dispose();

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("Something broke");
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void AReport_HasTheError_TheLogTail_AndSaysNothingWasSent()
    {
        File.WriteAllLines(Path.Combine(_paths.LogsDirectory, "frameforge-20261125.log"), Enumerable.Range(1, 250).Select(i => $"line {i}"));

        var path = _reports.Write(Thrown(), "Unhandled error; FrameForge closed");

        var text = File.ReadAllText(path!);
        Assert.Contains("Something broke", text, StringComparison.Ordinal);
        Assert.Contains("Unhandled error; FrameForge closed", text, StringComparison.Ordinal);
        Assert.Contains("FrameForge never sends it", text, StringComparison.Ordinal);
        Assert.Contains("line 250", text, StringComparison.Ordinal);
        Assert.Contains("line 51", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 50" + Environment.NewLine, text, StringComparison.Ordinal); // only the last 200
    }

    [Fact]
    public void Redact_RemovesTheProfileFolder_UserName_AndComputerName()
    {
        var text = @"Loading C:\Users\Byron\Pictures\a.jpg on BYRON-PC for byron";

        var redacted = FileCrashReports.Redact(text, @"C:\Users\Byron", "byron", "BYRON-PC");

        Assert.Equal(@"Loading %USERPROFILE%\Pictures\a.jpg on <computer> for <user>", redacted);
    }

    [Fact]
    public void Unseen_ListsNewReportsNewestFirst_UntilMarkedSeen()
    {
        var first = _reports.Write(Thrown(), "one")!;
        _time.UtcNow += TimeSpan.FromMinutes(1);
        var second = _reports.Write(Thrown(), "two")!;

        Assert.Equal([second, first], _reports.Unseen());

        _reports.MarkSeen();

        Assert.Empty(_reports.Unseen());
        Assert.Equal(2, Directory.EnumerateFiles(_paths.CrashReportsDirectory).Count()); // kept, just seen
    }

    [Fact]
    public void OnlyTheNewestTenAreKept()
    {
        for (var i = 0; i < FileCrashReports.Kept + 3; i++)
        {
            _reports.Write(Thrown(), "again");
            _time.UtcNow += TimeSpan.FromSeconds(1);
        }

        Assert.Equal(FileCrashReports.Kept, Directory.EnumerateFiles(_paths.CrashReportsDirectory).Count());
    }

    [Fact]
    public void NoReportsYet_IsFine()
    {
        Assert.Empty(_reports.Unseen());
        _reports.MarkSeen();
    }
}
