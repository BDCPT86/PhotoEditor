namespace FrameForge.Analysis;

/// <summary>
/// The clipping overlay (M09 C2, the J key): a copy of the image on screen with its clipped pixels painted, highlights
/// red and shadows blue. Display only; exports render from the recipe and never see it.
/// </summary>
/// <remarks>
/// Uses the levels of <see cref="ImageHistogram"/> and the ◐ badge (<see cref="ExposureAnalyzer.HighlightLevel"/>,
/// <see cref="ExposureAnalyzer.ShadowLevel"/>), so what is painted is exactly what the histogram's shares count.
/// </remarks>
public static class ClippingPainter
{
    /// <summary>Highlights: any channel at 254–255. A strong red, as BGR.</summary>
    public static readonly (byte B, byte G, byte R) HighlightColour = (0x30, 0x3B, 0xFF);

    /// <summary>Shadows: every channel at 0–1. A strong blue, as BGR.</summary>
    public static readonly (byte B, byte G, byte R) ShadowColour = (0xFF, 0x7B, 0x2F);

    /// <summary>A new image, the same size, with clipped pixels painted; the source is not changed.</summary>
    public static AnalysisImage Paint(AnalysisImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pixels = (byte[])image.Bgra.Clone();
        for (var p = 0; p + 3 < pixels.Length; p += 4)
        {
            byte b = pixels[p], g = pixels[p + 1], r = pixels[p + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var colour = max >= ExposureAnalyzer.HighlightLevel ? HighlightColour
                : max <= ExposureAnalyzer.ShadowLevel ? ShadowColour
                : ((byte B, byte G, byte R)?)null;
            if (colour is { } c)
            {
                pixels[p] = c.B;
                pixels[p + 1] = c.G;
                pixels[p + 2] = c.R;
                pixels[p + 3] = 255;
            }
        }

        return new AnalysisImage(image.Width, image.Height, pixels);
    }
}
