namespace FrameForge.Analysis;

/// <param name="MeanLuminance">Average luma, 0 (black) – 255 (white).</param>
/// <param name="HighlightClippedPercent">Pixels with any channel at 254–255: detail likely lost in highlights.</param>
/// <param name="ShadowClippedPercent">Pixels with every channel at 0–1: detail likely lost in shadows.</param>
/// <param name="Histogram">Luma histogram in 16 equal bins (counts).</param>
public sealed record ExposureResult(
    double MeanLuminance,
    double HighlightClippedPercent,
    double ShadowClippedPercent,
    int[] Histogram);

/// <summary>
/// Exposure indicators measured on the decoded JPEG (already tone-mapped by the camera), so "clipped" means
/// clipped in this JPEG, not necessarily in the sensor data.
/// </summary>
public static class ExposureAnalyzer
{
    public const string Version = "luma-mean-channel-clip-v1";
    public const byte HighlightLevel = 254;
    public const byte ShadowLevel = 1;
    public const int HistogramBins = 16;

    public static ExposureResult Analyze(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var px = image.Bgra;
        var total = image.Width * image.Height;
        long highlights = 0, shadows = 0;
        double lumaSum = 0;
        var histogram = new int[HistogramBins];

        for (var p = 0; p < px.Length; p += 4)
        {
            byte b = px[p], g = px[p + 1], r = px[p + 2];
            var max = Math.Max(r, Math.Max(g, b));
            if (max >= HighlightLevel)
            {
                highlights++;
            }

            if (max <= ShadowLevel)
            {
                shadows++;
            }

            var luma = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            lumaSum += luma;
            histogram[Math.Min(HistogramBins - 1, (int)(luma * HistogramBins / 256))]++;
        }

        return new ExposureResult(
            MeanLuminance: Math.Round(lumaSum / total, 2),
            HighlightClippedPercent: Math.Round(100.0 * highlights / total, 3),
            ShadowClippedPercent: Math.Round(100.0 * shadows / total, 3),
            Histogram: histogram);
    }
}
