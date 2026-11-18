using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// Deterministic mask cache key: the photo's identity (path, size, modified time), what makes the mask, the analysis
/// size and the clicks. Any change gives a new key, so a stale mask is never reused. Inversion is not part of the key:
/// Subject and Background share one mask.
/// </summary>
public static class MaskKey
{
    public static string Compute(Photo photo, string source, IReadOnlyList<MaskPoint> points, int longSide)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(points);

        var text = new StringBuilder();
        text.Append(photo.FilePath.ToUpperInvariant()).Append('|')
            .Append(photo.FileSizeBytes.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(photo.LastModifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(source).Append('|')
            .Append(longSide.ToString(CultureInfo.InvariantCulture));
        foreach (var p in points)
        {
            text.Append('|')
                .Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(p.Include ? '+' : '-');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())).AsSpan(0, 16));
    }
}
