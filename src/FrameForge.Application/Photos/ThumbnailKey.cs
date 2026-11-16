using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Photos;

/// <summary>
/// Deterministic thumbnail cache key. Any change to the source file identity, the requested
/// size, or the generator version produces a different key, so stale thumbnails are never reused.
/// </summary>
public static class ThumbnailKey
{
    public static string Compute(Photo photo, int maxPixelSize, string generatorVersion)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorVersion);

        var identity = string.Join(
            '|',
            photo.FilePath.ToUpperInvariant(),
            photo.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
            photo.LastModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            maxPixelSize.ToString(CultureInfo.InvariantCulture),
            generatorVersion);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
