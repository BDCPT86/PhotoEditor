namespace FrameForge.Application.Diagnostics;

/// <summary>
/// Crash reports kept on this computer (ADR 0019). FrameForge never sends them; the user can open or copy one to share
/// it themselves.
/// </summary>
public interface ICrashReports
{
    /// <summary>Reports the user hasn't been told about yet, newest first (full paths).</summary>
    IReadOnlyList<string> Unseen();

    /// <summary>The user has seen the notice: the current reports stop being "unseen". The files are kept.</summary>
    void MarkSeen();

    /// <summary>The text of a report, or null if it can't be read.</summary>
    string? Read(string path);
}
