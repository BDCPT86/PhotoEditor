using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FrameForge.Analysis;

/// <summary>
/// Settings that change computed values. They are part of each result's cache key, so changing them makes
/// stored results stale. Thresholds that only interpret values live in <c>AnalysisThresholds</c> instead.
/// </summary>
public sealed record AnalysisSettings
{
    public static readonly IReadOnlyList<int> AllowedPixelSizes = [512, 1024, 2048];

    public static AnalysisSettings Default { get; } = new();

    /// <summary>Longest side of the decoded image the metrics are computed on.</summary>
    public int MaxPixelSize { get; init; } = 1024;

    /// <summary>Sharpness is measured per cell of an N×N grid; the score is the sharpest cell.</summary>
    public int SharpnessGrid { get; init; } = 4;

    public string SharpnessSettingsHash => Hash($"max={MaxPixelSize};grid={SharpnessGrid}");

    public string ExposureSettingsHash => Hash($"max={MaxPixelSize}");

    public string PerceptualHashSettingsHash => Hash($"max={MaxPixelSize}");

    /// <summary>File content hashing has no settings.</summary>
    public static string ContentHashSettingsHash => Hash("none");

    private static string Hash(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString(CultureInfo.InvariantCulture))).AsSpan(0, 8));
}
