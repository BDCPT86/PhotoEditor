namespace FrameForge.Core.Photos;

public enum ImportPhase
{
    Scanning = 0,
    Reading = 1,
    Finishing = 2,
}

/// <summary>Snapshot of import progress, safe to hand to the UI.</summary>
/// <param name="Total">Files that need reading (new or changed). Unchanged files are not counted.</param>
/// <param name="Processed">Files read so far, including failures.</param>
public sealed record ImportProgress(ImportPhase Phase, int Discovered, int Total, int Processed, int Failed);
