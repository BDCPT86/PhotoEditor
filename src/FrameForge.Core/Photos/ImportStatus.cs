namespace FrameForge.Core.Photos;

/// <summary>Outcome of the most recent import attempt for a photo record.</summary>
public enum ImportStatus
{
    /// <summary>File was read successfully and its metadata is current.</summary>
    Imported = 0,

    /// <summary>File exists but could not be read (corrupt, unsupported, or inaccessible).</summary>
    Failed = 1,

    /// <summary>File was not found at its recorded path during the last import of its folder.</summary>
    Missing = 2,
}
