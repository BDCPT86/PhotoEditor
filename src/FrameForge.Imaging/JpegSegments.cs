using System.IO;

namespace FrameForge.Imaging;

/// <summary>Reads JPEG marker segments directly; WPF does not say whether a colour context came from an ICC profile or EXIF.</summary>
internal static class JpegSegments
{
    private static ReadOnlySpan<byte> IccSignature => "ICC_PROFILE\0"u8;

    /// <summary>
    /// True when the JPEG has an APP2 "ICC_PROFILE" segment before its image data. Reads only the headers; leaves the
    /// stream position wherever it stopped. Anything that isn't a well-formed JPEG header gives false.
    /// </summary>
    public static bool HasIccProfile(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> marker = stackalloc byte[4];
        Span<byte> signature = stackalloc byte[12];
        if (!ReadExactly(stream, marker[..2]) || marker[0] != 0xFF || marker[1] != 0xD8)
        {
            return false;
        }

        while (ReadExactly(stream, marker))
        {
            if (marker[0] != 0xFF)
            {
                return false;
            }

            var type = marker[1];
            if (type == 0xFF)
            {
                // Fill byte before a marker: step back to the second 0xFF and read the marker again from there.
                stream.Seek(-3, SeekOrigin.Current);
                continue;
            }

            if (type is 0xDA or 0xD9)
            {
                return false; // image data (or end) reached: metadata segments come before it
            }

            var length = (marker[2] << 8) | marker[3]; // includes these two length bytes
            if (length < 2)
            {
                return false;
            }

            var payload = length - 2;
            if (type == 0xE2 && payload >= signature.Length)
            {
                if (!ReadExactly(stream, signature))
                {
                    return false;
                }

                if (signature.SequenceEqual(IccSignature))
                {
                    return true;
                }

                payload -= signature.Length;
            }

            stream.Seek(payload, SeekOrigin.Current);
        }

        return false;
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer) => stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
}
