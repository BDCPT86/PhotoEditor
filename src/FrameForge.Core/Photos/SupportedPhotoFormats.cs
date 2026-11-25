namespace FrameForge.Core.Photos;

/// <summary>
/// File types the importer accepts: JPEG, and Canon CR3 RAW files (read through LibRaw and shown through their
/// embedded full-size JPEG; ADR 0020). CR3 is the only RAW format tested (Canon EOS R50), so it is the only one on.
/// </summary>
public static class SupportedPhotoFormats
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cr3" };

    private static readonly HashSet<string> Extensions = new([".jpg", ".jpeg", .. RawExtensions], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> FileExtensions => Extensions;

    public static bool IsSupported(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    /// <summary>A camera RAW file, read through LibRaw rather than Windows' image decoders.</summary>
    public static bool IsRaw(string path) =>
        !string.IsNullOrEmpty(path) && RawExtensions.Contains(Path.GetExtension(path));
}
