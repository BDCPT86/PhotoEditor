namespace FrameForge.Core.Photos;

/// <summary>
/// File types the importer accepts: JPEG, and Canon CR3 RAW files (read through LibRaw and shown through their
/// embedded full-size JPEG; ADR 0020). CR3 is the only RAW format tested (Canon EOS R50), so it is the only one on.
/// </summary>
public static class SupportedPhotoFormats
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cr3" };

    private static readonly HashSet<string> Extensions = new([".jpg", ".jpeg", .. RawExtensions], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Photo formats FrameForge recognises but doesn't import: other cameras' RAW files and image formats without a
    /// tested path. Import counts these so it can say what it skipped; other files (videos, sidecars, …) are ignored.
    /// </summary>
    private static readonly HashSet<string> KnownUnsupported = new(
        [
            ".cr2", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".raf", ".orf", ".rw2", ".pef", ".dng", ".srw",
            ".3fr", ".iiq", ".erf", ".mrw", ".x3f", ".rwl", ".kdc", ".dcr",
            ".heic", ".heif", ".hif", ".tif", ".tiff", ".png", ".webp",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> FileExtensions => Extensions;

    /// <summary>A photo FrameForge recognises as one but doesn't import (another RAW format, HEIC, TIFF, …).</summary>
    public static bool IsKnownUnsupported(string path) =>
        !string.IsNullOrEmpty(path) && KnownUnsupported.Contains(Path.GetExtension(path));

    public static bool IsSupported(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    /// <summary>A camera RAW file, read through LibRaw rather than Windows' image decoders.</summary>
    public static bool IsRaw(string path) =>
        !string.IsNullOrEmpty(path) && RawExtensions.Contains(Path.GetExtension(path));
}
