namespace FrameForge.Core.Photos;

/// <summary>
/// A RAW file and the JPEG the camera saved with it, shown and decided on as one photo (M09). The JPEG is the
/// <see cref="Cover"/>: its pixels are the RAW file's embedded preview, and it carries all of the camera's metadata.
/// </summary>
public sealed record PhotoPair(Photo Cover, Photo Raw);

/// <summary>The pairs found in a library, with lookups by photo.</summary>
public sealed class PhotoPairs
{
    public static PhotoPairs None { get; } = new([]);

    private readonly Dictionary<Guid, PhotoPair> _byCover;
    private readonly Dictionary<Guid, PhotoPair> _byRaw;

    internal PhotoPairs(IReadOnlyList<PhotoPair> pairs)
    {
        Pairs = pairs;
        _byCover = pairs.ToDictionary(p => p.Cover.Id);
        _byRaw = pairs.ToDictionary(p => p.Raw.Id);
    }

    public IReadOnlyList<PhotoPair> Pairs { get; }

    public int Count => Pairs.Count;

    /// <summary>The pair whose cover (JPEG) this photo is, if any.</summary>
    public PhotoPair? ByCover(Guid photoId) => _byCover.GetValueOrDefault(photoId);

    /// <summary>True for the RAW file of a pair: it is shown through its cover, not as a photo of its own.</summary>
    public bool IsHiddenRaw(Guid photoId) => _byRaw.ContainsKey(photoId);
}

/// <summary>
/// Finds RAW + JPEG pairs. Worked out in memory whenever the library loads, like similar groups (ADR 0004), so nothing
/// is stored and nothing can go stale.
/// </summary>
/// <remarks>
/// A RAW file and a JPEG pair when all of these hold:
/// <list type="bullet">
/// <item>they are in the same folder and have the same name apart from the extension, ignoring case
/// (<c>IMG_0603.CR3</c> and <c>IMG_0603.JPG</c>);</item>
/// <item>no other JPEG or RAW file has that name in that folder (<c>IMG_0603.JPG</c> and <c>IMG_0603.JPEG</c> make it
/// ambiguous, so none of them pair);</item>
/// <item>both were read (<see cref="ImportStatus.Imported"/>); a missing or unreadable file is shown on its own;</item>
/// <item>when both have a capture time, the times are the same to the second. This keeps apart files that share a
/// name only because the camera's counter came round to the same number on another day.</item>
/// </list>
/// </remarks>
public static class PhotoPairing
{
    public static PhotoPairs Find(IEnumerable<Photo> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        var groups = new Dictionary<(string Folder, string Name), (List<Photo> Jpegs, List<Photo> Raws)>(KeyComparer.Instance);
        foreach (var photo in photos)
        {
            var isRaw = SupportedPhotoFormats.IsRaw(photo.FilePath);
            if (!isRaw && !SupportedPhotoFormats.IsSupported(photo.FilePath))
            {
                continue;
            }

            var key = (photo.FolderPath.TrimEnd('\\', '/'), Path.GetFileNameWithoutExtension(photo.FileName));
            if (!groups.TryGetValue(key, out var group))
            {
                group = ([], []);
                groups[key] = group;
            }

            (isRaw ? group.Raws : group.Jpegs).Add(photo);
        }

        var pairs = new List<PhotoPair>();
        foreach (var (jpegs, raws) in groups.Values)
        {
            if (jpegs.Count == 1 && raws.Count == 1 && IsPair(jpegs[0], raws[0]))
            {
                pairs.Add(new PhotoPair(jpegs[0], raws[0]));
            }
        }

        return pairs.Count == 0 ? PhotoPairs.None : new PhotoPairs(pairs);
    }

    private static bool IsPair(Photo jpeg, Photo raw)
    {
        if (jpeg.Status != ImportStatus.Imported || raw.Status != ImportStatus.Imported)
        {
            return false;
        }

        return jpeg.CaptureLocalTime is not { } a || raw.CaptureLocalTime is not { } b || ToSecond(a) == ToSecond(b);
    }

    private static long ToSecond(DateTime time) => time.Ticks / TimeSpan.TicksPerSecond;

    private sealed class KeyComparer : IEqualityComparer<(string Folder, string Name)>
    {
        public static KeyComparer Instance { get; } = new();

        public bool Equals((string Folder, string Name) x, (string Folder, string Name) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Folder, y.Folder) && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((string Folder, string Name) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Folder), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
