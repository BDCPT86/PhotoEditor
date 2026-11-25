using System.IO;
using System.Text;

namespace FrameForge.Imaging.Tests;

/// <summary>
/// Builds a minimal ICC v2 RGB display profile (matrix + gamma 2.2) at test time. With <c>swapRedAndBlue</c> the red
/// and blue colorants trade places, so a colour-managed decode turns red pixels blue: an unmistakable sign that the
/// profile was applied (or, in an output file, that it was wrongly carried over).
/// </summary>
internal static class TestIcc
{
    public static byte[] Build(bool swapRedAndBlue)
    {
        byte[] red = Xyz(0.4361, 0.2225, 0.0139), green = Xyz(0.3851, 0.7169, 0.0971), blue = Xyz(0.1431, 0.0606, 0.7141);
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", Description("FrameForge test profile")),
            ("wtpt", Xyz(0.9642, 1.0, 0.8249)),
            ("rXYZ", swapRedAndBlue ? blue : red),
            ("gXYZ", green),
            ("bXYZ", swapRedAndBlue ? red : blue),
            ("rTRC", Gamma22()),
            ("gTRC", Gamma22()),
            ("bTRC", Gamma22()),
            ("cprt", [.. "text"u8, 0, 0, 0, 0, .. "none"u8, 0, 0, 0, 0]),
        };

        const int headerLength = 128;
        var tableLength = 4 + (tags.Count * 12);
        var body = new List<byte>();
        var table = new List<byte>(BigEndian((uint)tags.Count));
        foreach (var (signature, data) in tags)
        {
            while ((headerLength + tableLength + body.Count) % 4 != 0)
            {
                body.Add(0);
            }

            table.AddRange(Encoding.ASCII.GetBytes(signature));
            table.AddRange(BigEndian((uint)(headerLength + tableLength + body.Count)));
            table.AddRange(BigEndian((uint)data.Length));
            body.AddRange(data);
        }

        var header = new byte[headerLength];
        BigEndian((uint)(headerLength + tableLength + body.Count)).CopyTo(header, 0);
        new byte[] { 2, 0x10, 0, 0 }.CopyTo(header, 8);
        "mntr"u8.CopyTo(header.AsSpan(12));
        "RGB "u8.CopyTo(header.AsSpan(16));
        "XYZ "u8.CopyTo(header.AsSpan(20));
        "acsp"u8.CopyTo(header.AsSpan(36));
        Fixed(0.9642).CopyTo(header, 68);
        Fixed(1.0).CopyTo(header, 72);
        Fixed(0.8249).CopyTo(header, 76);
        return [.. header, .. table, .. body];
    }

    /// <summary>Writes the profile to <paramref name="path"/> (WPF's ColorContext reads profiles from a file).</summary>
    public static string WriteFile(string path, bool swapRedAndBlue)
    {
        File.WriteAllBytes(path, Build(swapRedAndBlue));
        return path;
    }

    private static byte[] Xyz(double x, double y, double z) => [.. "XYZ "u8, 0, 0, 0, 0, .. Fixed(x), .. Fixed(y), .. Fixed(z)];

    private static byte[] Gamma22() => [.. "curv"u8, 0, 0, 0, 0, 0, 0, 0, 1, 0x02, 0x33, 0, 0];

    private static byte[] Description(string text) =>
        [.. "desc"u8, 0, 0, 0, 0, .. BigEndian((uint)text.Length + 1), .. Encoding.ASCII.GetBytes(text), 0, .. new byte[4 + 4 + 2 + 1 + 67]];

    private static byte[] Fixed(double value) => BigEndian((uint)(int)Math.Round(value * 65536));

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
}
