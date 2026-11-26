namespace FrameForge.Application.Cleanup;

/// <summary>Why a file can't be moved to the Recycle Bin. Such files are skipped and left where they are, never deleted.</summary>
public enum RecycleRefusal
{
    None,

    /// <summary>The file isn't there any more.</summary>
    Missing,

    /// <summary>A network share, a removable drive (SD card, most USB drives) or another drive without a Recycle Bin.</summary>
    NoRecycleBinOnDrive,

    /// <summary>The drive can't be reached right now.</summary>
    DriveUnavailable,

    /// <summary>The Recycle Bin is turned off for this drive, so Windows would delete files permanently.</summary>
    RecycleBinOff,

    /// <summary>Windows has no Recycle Bin settings for this drive, so it can't be confirmed that files would be recycled.</summary>
    RecycleBinUnknown,

    /// <summary>The file is larger than the drive's Recycle Bin can hold, so Windows would delete it permanently.</summary>
    TooLarge,
}

/// <summary>A file checked before moving: its size, and why it would be skipped (<see cref="RecycleRefusal.None"/> when it can go).</summary>
public sealed record RecycleCheck(string Path, long Size, RecycleRefusal Refusal)
{
    public bool CanMove => Refusal == RecycleRefusal.None;
}

public enum RecycleOutcome
{
    /// <summary>Moved, and confirmed: the file is gone and the Recycle Bin holds one more item.</summary>
    Moved,

    /// <summary>Not moved, for the reason given; the file is untouched.</summary>
    Skipped,

    /// <summary>Windows didn't move it (in use, access denied, declined); the file is still there.</summary>
    Failed,

    /// <summary>
    /// The file is gone but the Recycle Bin didn't gain an item, so it can't be confirmed it was recycled. Moving stops
    /// at once. (Windows emptying older items to make room gives the same signal.)
    /// </summary>
    NotConfirmed,

    /// <summary>Not tried: moving stopped (cancelled, or after <see cref="NotConfirmed"/>).</summary>
    NotTried,
}

public sealed record RecycleFileResult(string Path, RecycleOutcome Outcome, RecycleRefusal Refusal = RecycleRefusal.None, string? Detail = null);

public sealed record RecycleProgress(int Total, int Done, string? CurrentFile);

public sealed record RecycleSummary(IReadOnlyList<RecycleFileResult> Files, bool Cancelled)
{
    public int Moved => Files.Count(f => f.Outcome == RecycleOutcome.Moved);

    public int Skipped => Files.Count(f => f.Outcome == RecycleOutcome.Skipped);

    public int Failed => Files.Count(f => f.Outcome == RecycleOutcome.Failed);

    /// <summary>Moving stopped because a move couldn't be confirmed.</summary>
    public bool StoppedForSafety => Files.Any(f => f.Outcome == RecycleOutcome.NotConfirmed);
}

/// <summary>
/// Moves files to the Windows Recycle Bin, the only way FrameForge ever removes a photo (M09). Never deletes a file
/// permanently: a file on a drive where it can't be recycled is skipped. Files are moved one at a time, and each move
/// is confirmed before the next.
/// </summary>
public interface IRecycleBin
{
    /// <summary>Checks the files without changing anything: for the confirmation panel.</summary>
    Task<IReadOnlyList<RecycleCheck>> CheckAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);

    /// <summary>Moves the files that pass the check (checked again just before each move).</summary>
    Task<RecycleSummary> MoveAsync(IReadOnlyList<string> paths, IProgress<RecycleProgress>? progress, CancellationToken cancellationToken);
}
