namespace FrameForge.Core.Photos;

public sealed record ImportSummary
{
    public required string FolderPath { get; init; }

    /// <summary>Supported files found in the folder.</summary>
    public int Discovered { get; init; }

    /// <summary>New library records created.</summary>
    public int Added { get; init; }

    /// <summary>Existing records re-read because the file changed or previously failed.</summary>
    public int Updated { get; init; }

    /// <summary>Existing records skipped because the file is unchanged.</summary>
    public int Unchanged { get; init; }

    /// <summary>Files that could not be read; recorded with <see cref="ImportStatus.Failed"/>.</summary>
    public int Failed { get; init; }

    /// <summary>Existing records whose files were not found and are now <see cref="ImportStatus.Missing"/>.</summary>
    public int MarkedMissing { get; init; }

    /// <summary>
    /// Records that were not marked missing because the scan found no photos at all where the library has some (an
    /// empty network share or a drive mounted without its files, say). Zero unless that happened.
    /// </summary>
    public int MissingCheckSkipped { get; init; }

    /// <summary>Sub-folders that could not be read (permissions, I/O errors). Their photos are left untouched.</summary>
    public IReadOnlyList<string> SkippedFolders { get; init; } = [];

    /// <summary>Photos left out because FrameForge doesn't import their format, by extension (".NEF" → 3).</summary>
    public IReadOnlyDictionary<string, int> UnsupportedFiles { get; init; } = new Dictionary<string, int>();

    public bool WasCancelled { get; init; }

    public TimeSpan Duration { get; init; }
}
