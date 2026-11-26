using FrameForge.Application.Cleanup;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Cleanup;

/// <summary>A drive as far as recycling is concerned.</summary>
/// <param name="Root">The drive's root, such as <c>C:\</c>.</param>
/// <param name="IsLocalFixed">A local, non-removable drive: the only kind FrameForge recycles on.</param>
/// <param name="IsReady">The drive can be read.</param>
/// <param name="Bin">The drive's Recycle Bin settings, or null when Windows has none for it.</param>
public sealed record RecycleDrive(string Root, bool IsLocalFixed, bool IsReady, RecycleBinSettings? Bin);

/// <param name="Enabled">False when the drive is set to delete files immediately instead of recycling them.</param>
/// <param name="MaxBytes">The most the drive's Recycle Bin holds; larger files can't be recycled.</param>
public sealed record RecycleBinSettings(bool Enabled, long MaxBytes);

/// <summary>What Windows did with one file.</summary>
/// <param name="Succeeded">Windows reported success and nothing was aborted.</param>
/// <param name="Detail">Windows' error code or other detail when it didn't succeed.</param>
public sealed record ShellRecycleResult(bool Succeeded, string? Detail);

/// <summary>The Windows calls behind <see cref="RecycleBinService"/>, so its rules can be tested without the real Recycle Bin.</summary>
public interface IRecycleBinPlatform
{
    /// <summary>The drive holding <paramref name="path"/>, or null for a path without a drive letter (a network share).</summary>
    RecycleDrive? GetDrive(string path);

    /// <summary>The file's size, or null when it doesn't exist.</summary>
    long? FileSize(string path);

    /// <summary>How many items the drive's Recycle Bin holds, or null when that can't be read.</summary>
    long? ItemsInRecycleBin(string root);

    /// <summary>Asks Windows to move one file to the Recycle Bin, refusing any permanent delete.</summary>
    ShellRecycleResult Recycle(string path);
}

/// <summary>
/// The rules for moving files to the Recycle Bin. Defence in depth against a permanent delete:
/// <list type="number">
/// <item>Before any move: only files on local fixed drives whose Recycle Bin is on and large enough
/// (<see cref="Refusal"/>), checked again just before each file.</item>
/// <item>The move itself asks Windows to warn rather than delete permanently (<see cref="IRecycleBinPlatform.Recycle"/>).</item>
/// <item>After each move: the file must be gone and the Recycle Bin must hold one more item; otherwise moving stops.</item>
/// </list>
/// </summary>
public sealed partial class RecycleBinService(IRecycleBinPlatform platform, ILogger<RecycleBinService> logger) : IRecycleBin
{
    public Task<IReadOnlyList<RecycleCheck>> CheckAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Task.Run<IReadOnlyList<RecycleCheck>>(
            () =>
            {
                var checks = new List<RecycleCheck>(paths.Count);
                foreach (var path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    checks.Add(Check(path));
                }

                return checks;
            },
            cancellationToken);
    }

    public Task<RecycleSummary> MoveAsync(IReadOnlyList<string> paths, IProgress<RecycleProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Task.Run(() => Move(paths, progress, cancellationToken), CancellationToken.None);
    }

    private RecycleSummary Move(IReadOnlyList<string> paths, IProgress<RecycleProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<RecycleFileResult>(paths.Count);
        var cancelled = false;
        LogMoveStarting(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }

            if (cancelled || results.Any(r => r.Outcome == RecycleOutcome.NotConfirmed))
            {
                results.Add(new RecycleFileResult(path, RecycleOutcome.NotTried));
                continue;
            }

            progress?.Report(new RecycleProgress(paths.Count, i, Path.GetFileName(path)));
            var result = MoveOne(path);
            results.Add(result);
            LogFileResult(path, result.Outcome, result.Refusal, result.Detail);
        }

        progress?.Report(new RecycleProgress(paths.Count, paths.Count, null));
        var summary = new RecycleSummary(results, cancelled);
        LogMoveFinished(summary.Moved, summary.Skipped, summary.Failed, summary.StoppedForSafety, cancelled);
        return summary;
    }

    private RecycleFileResult MoveOne(string path)
    {
        var check = Check(path);
        if (!check.CanMove)
        {
            return new RecycleFileResult(path, RecycleOutcome.Skipped, check.Refusal);
        }

        var root = platform.GetDrive(path)!.Root;
        var before = platform.ItemsInRecycleBin(root);
        if (before is null)
        {
            return new RecycleFileResult(path, RecycleOutcome.Skipped, RecycleRefusal.RecycleBinUnknown, "The Recycle Bin could not be read.");
        }

        var shell = platform.Recycle(path);
        var stillThere = platform.FileSize(path) is not null;
        if (stillThere)
        {
            return new RecycleFileResult(path, RecycleOutcome.Failed, Detail: shell.Detail ?? "Windows did not move the file.");
        }

        var after = platform.ItemsInRecycleBin(root);
        if (after is { } count && count > before)
        {
            return new RecycleFileResult(path, RecycleOutcome.Moved);
        }

        // Gone, but not confirmed in the Recycle Bin: stop before anything else happens to another file.
        LogNotConfirmed(path, before.Value, after);
        return new RecycleFileResult(path, RecycleOutcome.NotConfirmed, Detail: "The file is gone, but the Recycle Bin did not gain an item.");
    }

    private RecycleCheck Check(string path)
    {
        var size = platform.FileSize(path);
        if (size is not { } bytes)
        {
            return new RecycleCheck(path, 0, RecycleRefusal.Missing);
        }

        return new RecycleCheck(path, bytes, Refusal(platform.GetDrive(path), bytes));
    }

    /// <summary>Why a file of <paramref name="size"/> bytes on <paramref name="drive"/> can't be recycled, or None.</summary>
    internal static RecycleRefusal Refusal(RecycleDrive? drive, long size)
    {
        if (drive is null || !drive.IsLocalFixed)
        {
            return RecycleRefusal.NoRecycleBinOnDrive;
        }

        if (!drive.IsReady)
        {
            return RecycleRefusal.DriveUnavailable;
        }

        return drive.Bin switch
        {
            null => RecycleRefusal.RecycleBinUnknown,
            { Enabled: false } => RecycleRefusal.RecycleBinOff,
            { MaxBytes: var max } when size >= max => RecycleRefusal.TooLarge,
            _ => RecycleRefusal.None,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Moving {Count} file(s) to the Recycle Bin.")]
    private partial void LogMoveStarting(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recycle Bin: {Path}: {Outcome} {Refusal} {Detail}")]
    private partial void LogFileResult(string path, RecycleOutcome outcome, RecycleRefusal refusal, string? detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recycle Bin move finished: moved={Moved} skipped={Skipped} failed={Failed} stoppedForSafety={Stopped} cancelled={Cancelled}")]
    private partial void LogMoveFinished(int moved, int skipped, int failed, bool stopped, bool cancelled);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Path} is gone but the Recycle Bin did not gain an item ({Before} before, {After} after). Stopped moving files.")]
    private partial void LogNotConfirmed(string path, long before, long? after);
}
