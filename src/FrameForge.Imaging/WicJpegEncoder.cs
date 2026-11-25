using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Application.Editing;
using FrameForge.Core.Decisions;

namespace FrameForge.Imaging;

/// <summary>
/// Writes edited pixels as a JPEG with WIC, copying the original's metadata (EXIF, XMP, IPTC) when asked. The
/// original file is only read. What is adjusted, because the pixels no longer match the original file:
/// <list type="bullet">
/// <item>Orientation is reset to "normal": the pixels are already upright.</item>
/// <item>The embedded EXIF thumbnail is dropped: it shows the unedited photo.</item>
/// <item>Unrecognised segments are dropped. This includes the original's ICC colour profile (APP2), since the photo
/// loader already converted the pixels to sRGB with it (colour space tags are then set to sRGB to match), and Canon's
/// multi-picture (MPF) index, which points at data that isn't in the new file.</item>
/// <item>Optionally GPS and place names are removed, and the FrameForge rating and label are written.</item>
/// </list>
/// If the metadata cannot be carried over the image is still written, without it (see ADR 0017).
/// </summary>
public sealed class WicJpegEncoder : IJpegEncoder
{
    private const string ExifOrientation = "/app1/ifd/{ushort=274}";
    private const string XmpOrientation = "/xmp/tiff:Orientation";
    private const string ExifThumbnail = "/app1/thumb";
    private const string ExifGps = "/app1/ifd/gps";
    private const string ExifColorSpace = "/app1/ifd/exif/{ushort=40961}";
    private const string ExifInteropIndex = "/app1/ifd/exif/interop/{ushort=1}";
    private const string XmpColorSpace = "/xmp/exif:ColorSpace";
    private const string XmpIccProfileName = "/xmp/photoshop:ICCProfile";
    private const string XmpLabel = "/xmp/xmp:Label";
    private const string Xmp = "/xmp";
    private const string Iptc = "/app13/irb/8bimiptc/iptc";

    /// <summary>XMP properties that say where the photo was taken (GPS is matched by the "exif:GPS" prefix).</summary>
    private static readonly HashSet<string> XmpLocation = new(
        [
            "photoshop:City", "photoshop:State", "photoshop:Country",
            "Iptc4xmpCore:Location", "Iptc4xmpCore:CountryCode",
            "Iptc4xmpExt:LocationCreated", "Iptc4xmpExt:LocationShown",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>IPTC IIM datasets that say where the photo was taken.</summary>
    private static readonly HashSet<string> IptcLocation = new(
        ["City", "Sub-location", "Province/State", "Country/Primary Location Code", "Country/Primary Location Name"],
        StringComparer.OrdinalIgnoreCase);

    public bool Write(AnalysisImage image, JpegMetadata metadata, Stream destination, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgr32, null, image.Bgra, image.Width * 4);
        bitmap.Freeze();

        if (metadata.SourcePath is { } sourcePath)
        {
            try
            {
                // WIC reads cloned metadata lazily from the source, so the original stays open (read-only) until the
                // encode is done. Encode to memory first: if WIC rejects the metadata, nothing partial is written.
                using var source = WicFiles.OpenReadOnly(sourcePath);
                if (PrepareMetadata(source, metadata) is { } prepared)
                {
                    using var buffer = new MemoryStream();
                    Encode(bitmap, prepared, buffer, quality);
                    buffer.Position = 0;
                    buffer.CopyTo(destination);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || WicFiles.IsDecodeFailure(ex))
            {
                // Fall through to writing without the original's metadata.
            }
        }

        Encode(bitmap, metadata.Decision is { IsEmpty: false } decision ? DecisionOnly(decision) : null, destination, quality);
        return metadata.SourcePath is null;
    }

    private static void Encode(BitmapSource bitmap, BitmapMetadata? metadata, Stream destination, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        encoder.Save(destination);
    }

    /// <summary>The source's metadata, adjusted for the new file; valid only while <paramref name="stream"/> is open.</summary>
    private static BitmapMetadata? PrepareMetadata(Stream stream, JpegMetadata options)
    {
        // Scan the segments before WIC starts reading the stream lazily.
        var hasIccProfile = JpegSegments.HasIccProfile(stream);
        stream.Position = 0;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder is not JpegBitmapDecoder || decoder.Frames.Count == 0 || decoder.Frames[0].Metadata is not BitmapMetadata source)
        {
            return null;
        }

        var metadata = source.Clone();
        SetIfPresent(metadata, ExifOrientation, (ushort)1);
        SetIfPresent(metadata, XmpOrientation, "1");
        RemoveIfPresent(metadata, ExifThumbnail);

        // Unrecognised segments describe the original file: its ICC profile (APP2), Canon's multi-picture index, …
        foreach (var query in metadata.Where(q => q.StartsWith("/unknown", StringComparison.Ordinal)).ToList())
        {
            metadata.RemoveQuery(query);
        }

        // The loader converts pixels to sRGB only when the original embeds an ICC profile; then say sRGB. Without one
        // (e.g. a camera's Adobe RGB JPEG, marked only by EXIF tags) the pixels keep their colour space, and so do the tags.
        if (hasIccProfile)
        {
            SetIfPresent(metadata, ExifColorSpace, (ushort)1);
            SetIfPresent(metadata, ExifInteropIndex, "R98");
            SetIfPresent(metadata, XmpColorSpace, "1");
            RemoveIfPresent(metadata, XmpIccProfileName);
        }

        if (options.RemoveLocation)
        {
            RemoveLocation(metadata);
        }

        if (options.Decision is { } decision)
        {
            WriteDecision(metadata, decision);
        }

        return metadata;
    }

    private static void RemoveLocation(BitmapMetadata metadata)
    {
        RemoveIfPresent(metadata, ExifGps);
        RemoveChildren(metadata, Xmp, name => name.StartsWith("exif:GPS", StringComparison.OrdinalIgnoreCase) || XmpLocation.Contains(name));
        RemoveChildren(metadata, Iptc, name => IptcLocation.Contains(name.StartsWith("{str=", StringComparison.Ordinal) && name.EndsWith('}') ? name[5..^1] : name));
    }

    /// <summary>Removes the direct children of the block at <paramref name="block"/> whose name matches.</summary>
    private static void RemoveChildren(BitmapMetadata metadata, string block, Func<string, bool> matches)
    {
        if (!metadata.ContainsQuery(block) || metadata.GetQuery(block) is not BitmapMetadata children)
        {
            return;
        }

        foreach (var child in children.ToList())
        {
            if (matches(child.TrimStart('/')))
            {
                metadata.RemoveQuery(block + child);
            }
        }
    }

    /// <summary>Rating (EXIF and XMP, via Windows' property handler) and label (XMP, as Lightroom and Bridge read it).</summary>
    private static void WriteDecision(BitmapMetadata metadata, PhotoDecision decision)
    {
        metadata.Rating = decision.Rating ?? 0;
        if (decision.Label == ColourLabel.None)
        {
            RemoveIfPresent(metadata, XmpLabel);
        }
        else
        {
            metadata.SetQuery(XmpLabel, decision.Label.ToString());
        }
    }

    /// <summary>Metadata holding only the rating and label, for a source whose own metadata can't be copied.</summary>
    private static BitmapMetadata DecisionOnly(PhotoDecision decision)
    {
        var metadata = new BitmapMetadata("jpg");
        WriteDecision(metadata, decision);
        return metadata;
    }

    private static void SetIfPresent(BitmapMetadata metadata, string query, object value)
    {
        if (metadata.ContainsQuery(query))
        {
            metadata.SetQuery(query, value);
        }
    }

    private static void RemoveIfPresent(BitmapMetadata metadata, string query)
    {
        if (metadata.ContainsQuery(query))
        {
            metadata.RemoveQuery(query);
        }
    }
}
