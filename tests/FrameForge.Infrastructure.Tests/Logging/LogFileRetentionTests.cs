using FrameForge.Infrastructure.Logging;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Logging;

public sealed class LogFileRetentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly LogFileRetention _retention = new(new ManualTimeProvider(Now), NullLogger<LogFileRetention>.Instance);

    public void Dispose() => _temp.Dispose();

    private string CreateFile(string name, TimeSpan age)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, "log");
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime - age);
        return path;
    }

    [Fact]
    public void DeletesOnlyAppLogFilesOlderThanRetention()
    {
        var expired = CreateFile("frameforge-20260501.log", TimeSpan.FromDays(45));
        var expiredSizeRoll = CreateFile("frameforge-20260502_001.log", TimeSpan.FromDays(44));
        var recent = CreateFile("frameforge-20260610.log", TimeSpan.FromDays(5));
        var unrelated = CreateFile("notes.txt", TimeSpan.FromDays(90));
        var otherLog = CreateFile("other-20260101.log", TimeSpan.FromDays(90));

        var deleted = _retention.DeleteExpiredLogs(_temp.Path, retentionDays: 14);

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(expiredSizeRoll));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(otherLog));
    }

    [Fact]
    public void ReturnsZero_WhenDirectoryDoesNotExist() =>
        Assert.Equal(0, _retention.DeleteExpiredLogs(Path.Combine(_temp.Path, "missing"), retentionDays: 14));

    [Fact]
    public void RejectsNonPositiveRetention() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _retention.DeleteExpiredLogs(_temp.Path, retentionDays: 0));
}
