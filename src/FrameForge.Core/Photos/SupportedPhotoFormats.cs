namespace FrameForge.Core.Photos;

/// <summary>File types the importer accepts. JPEG only until RAW support is added.</summary>
public static class SupportedPhotoFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    public static IReadOnlyCollection<string> FileExtensions => Extensions;

    public static bool IsSupported(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
