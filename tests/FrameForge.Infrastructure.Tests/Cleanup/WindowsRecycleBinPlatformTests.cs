using System.Runtime.Versioning;
using FrameForge.Application.Cleanup;
using FrameForge.Infrastructure.Cleanup;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Cleanup;

/// <summary>
/// M09 B1 against real Windows. A network path is refused without touching anything; the rest needs
/// FRAMEFORGE_TEST_RECYCLE=1, because it reads this PC's drive settings and moves a temporary file of its own
/// (ff-recycle-test-*.tmp) into the real Recycle Bin.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBinPlatformTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool OptedIn => Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_RECYCLE") == "1";

    [Fact]
    public void ANetworkPath_HasNoDrive_SoItIsRefused()
    {
        var platform = new WindowsRecycleBinPlatform();

        Assert.Null(platform.GetDrive(@"\\server\photos\IMG_0001.JPG"));
        Assert.Equal(RecycleRefusal.NoRecycleBinOnDrive, RecycleBinService.Refusal(platform.GetDrive(@"\\server\photos\IMG_0001.JPG"), 1));
    }

    [Fact]
    public void TheTempFolderDrive_IsFixed_WithItsRecycleBinSettings()
    {
        Assert.SkipUnless(OptedIn, "Set FRAMEFORGE_TEST_RECYCLE=1 to read this PC's Recycle Bin settings.");
        var drive = new WindowsRecycleBinPlatform().GetDrive(Path.GetTempPath());

        Assert.NotNull(drive);
        Assert.True(drive.IsLocalFixed);
        Assert.True(drive.IsReady);
        Assert.NotNull(drive.Bin);
        Assert.True(drive.Bin.MaxBytes > 0);
    }

    [Fact]
    public async Task ATempFile_GoesToTheRecycleBin_AndTheMoveIsConfirmed()
    {
        Assert.SkipUnless(OptedIn, "Set FRAMEFORGE_TEST_RECYCLE=1 to move a temporary file into the real Recycle Bin.");
        var path = Path.Combine(Path.GetTempPath(), $"ff-recycle-test-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(path, new byte[1024], Ct);
        var platform = new WindowsRecycleBinPlatform();
        var before = platform.ItemsInRecycleBin(platform.GetDrive(path)!.Root);
        var service = new RecycleBinService(platform, NullLogger<RecycleBinService>.Instance);

        var summary = await service.MoveAsync([path], null, Ct);

        Assert.Equal(RecycleOutcome.Moved, Assert.Single(summary.Files).Outcome);
        Assert.False(File.Exists(path));
        Assert.True(platform.ItemsInRecycleBin(platform.GetDrive(path)!.Root) > before);
    }
}
