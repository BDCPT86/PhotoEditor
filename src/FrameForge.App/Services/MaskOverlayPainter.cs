using FrameForge.Analysis;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.App.Services;

/// <summary>
/// Draws a mask over the rendered preview so the selection is easy to judge, the same look as the grading sheets:
/// inside the mask the photo is unchanged, outside it is greyed and darkened, the 50 % edge is outlined in amber, and
/// each click is marked with a ring holding + (add) or − (remove), so the two never differ by colour alone.
/// </summary>
internal static class MaskOverlayPainter
{
    private const float OutsideBrightness = 0.3f;

    public static AnalysisImage Paint(AnalysisImage source, EditRecipe recipe, AnalysisImage rendered, MaskImage mask, bool invert, IReadOnlyList<MaskPoint> points)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(points);

        var alpha = EditRenderer.RenderMask(source, recipe, mask, invert, Math.Max(rendered.Width, rendered.Height));
        int w = rendered.Width, h = rendered.Height;
        if (alpha.Width != w || alpha.Height != h)
        {
            return rendered; // a size mismatch would misplace the overlay; show the photo as it is
        }

        var src = rendered.Bgra;
        var dst = new byte[src.Length];
        for (var i = 0; i < w * h; i++)
        {
            var p = i * 4;
            var a = alpha.Alpha[i] / 255f;
            var grey = ((0.0722f * src[p]) + (0.7152f * src[p + 1]) + (0.2126f * src[p + 2])) * OutsideBrightness;
            for (var c = 0; c < 3; c++)
            {
                dst[p + c] = (byte)Math.Clamp((int)MathF.Round((src[p + c] * a) + (grey * (1 - a))), 0, 255);
            }

            dst[p + 3] = 255;
        }

        // Amber outline where the mask crosses 50 %, two pixels wide on large previews so it stays visible.
        var thickness = Math.Max(w, h) >= 1500 ? 2 : 1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var inside = alpha.Alpha[(y * w) + x] >= 128;
                var edge = (x + 1 < w && (alpha.Alpha[(y * w) + x + 1] >= 128) != inside) || (y + 1 < h && (alpha.Alpha[((y + 1) * w) + x] >= 128) != inside);
                if (edge)
                {
                    for (var t = 0; t < thickness && x + t < w; t++)
                    {
                        SetPixel(dst, w, x + t, y, (255, 190, 40));
                    }
                }
            }
        }

        var geometry = new EditGeometry(source.Width, source.Height, recipe);
        var radius = Math.Max(7, Math.Max(w, h) / 120);
        foreach (var point in points)
        {
            var (u, v) = geometry.ToOutput(point.X * source.Width, point.Y * source.Height);
            if (u is >= 0 and <= 1 && v is >= 0 and <= 1)
            {
                DrawMarker(dst, w, h, (int)(u * w), (int)(v * h), radius, point.Include);
            }
        }

        return new AnalysisImage(w, h, dst);
    }

    /// <summary>A white ring with a dark outline, holding a + (add) or − (remove).</summary>
    internal static void DrawMarker(byte[] bgra, int w, int h, int cx, int cy, int radius, bool include)
    {
        (byte R, byte G, byte B) dark = (20, 20, 20);
        (byte R, byte G, byte B) white = (255, 255, 255);
        for (var dy = -radius - 2; dy <= radius + 2; dy++)
        {
            for (var dx = -radius - 2; dx <= radius + 2; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= w || y >= h)
                {
                    continue;
                }

                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d <= radius + 2 && d > radius + 0.5)
                {
                    SetPixel(bgra, w, x, y, dark);
                }
                else if (d <= radius + 0.5 && d > radius - 1.5)
                {
                    SetPixel(bgra, w, x, y, white);
                }
                else if (d <= radius - 1.5)
                {
                    // Inside: a dark disc with a white + or −.
                    var arm = radius - 3;
                    var onBar = Math.Abs(dy) <= 1 && Math.Abs(dx) <= arm;
                    var onStem = include && Math.Abs(dx) <= 1 && Math.Abs(dy) <= arm;
                    SetPixel(bgra, w, x, y, onBar || onStem ? white : dark);
                }
            }
        }
    }

    private static void SetPixel(byte[] bgra, int w, int x, int y, (byte R, byte G, byte B) colour)
    {
        var p = ((y * w) + x) * 4;
        bgra[p] = colour.B;
        bgra[p + 1] = colour.G;
        bgra[p + 2] = colour.R;
        bgra[p + 3] = 255;
    }
}
