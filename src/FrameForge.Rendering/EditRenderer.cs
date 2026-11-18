using FrameForge.Analysis;
using FrameForge.Core.Editing;

namespace FrameForge.Rendering;

/// <summary>
/// Renders a recipe onto an oriented 8-bit BGRA image. Preview and export both call this; they differ only in the
/// source resolution and output size. Pure and deterministic; parallel over rows. Never modifies <c>source</c>.
/// </summary>
public static class EditRenderer
{
    /// <summary>
    /// Version of the rendering maths, recorded with cached edited thumbnails. v2 (Milestone 08) adds local
    /// adjustments; recipes without them render exactly as in v1.
    /// </summary>
    public const string Version = "render-v2";

    /// <param name="source">The photo, upright (EXIF orientation applied), at any resolution.</param>
    /// <param name="recipe">A valid recipe.</param>
    /// <param name="maxLongSide">Optional limit on the output's longest side; never upscales.</param>
    public static AnalysisImage Render(AnalysisImage source, EditRecipe recipe, int? maxLongSide = null, CancellationToken cancellationToken = default) =>
        Render(source, recipe, null, maxLongSide, cancellationToken);

    /// <summary>
    /// Renders a recipe including its local adjustments. Each local adjustment needs its mask in
    /// <paramref name="masks"/> (by <see cref="LocalAdjustment.Id"/>, not inverted, over the upright photo before crop,
    /// at any resolution with the photo's aspect ratio); an adjustment without one is left out, so the rest of the edit
    /// still renders.
    /// </summary>
    /// <remarks>
    /// Per pixel, in linear light: the global settings first, then each local adjustment in order, blended by its
    /// refined mask: <c>c ← c + α·(local(c) − c)</c>. See <see cref="MaskField"/> for how a mask follows geometry and
    /// edges.
    /// </remarks>
    public static AnalysisImage Render(AnalysisImage source, EditRecipe recipe, IReadOnlyDictionary<Guid, MaskImage>? masks, int? maxLongSide = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.EnsureValid();

        var geometry = new EditGeometry(source.Width, source.Height, recipe);
        var (width, height) = geometry.OutputSize(maxLongSide);
        var identity = !recipe.HasGeometry && width == source.Width && height == source.Height;

        var locals = LocalsToApply(source, geometry, recipe, masks, cancellationToken);
        var output = identity ? (byte[])source.Bgra.Clone() : Resample(source, geometry, width, height, cancellationToken);
        if (recipe.HasTone || recipe.HasColour || locals.Count > 0)
        {
            ApplyTone(output, width, height, new ToneOps(recipe), locals, cancellationToken);
        }

        return new AnalysisImage(width, height, output);
    }

    /// <summary>
    /// The refined mask exactly as <see cref="Render(AnalysisImage, EditRecipe, IReadOnlyDictionary{Guid, MaskImage}?, int?, CancellationToken)"/>
    /// uses it, at the output's size and framing (for showing the mask over the photo).
    /// </summary>
    public static MaskImage RenderMask(AnalysisImage source, EditRecipe recipe, MaskImage mask, bool invert, int? maxLongSide = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(mask);

        var geometry = new EditGeometry(source.Width, source.Height, recipe);
        var (width, height) = geometry.OutputSize(maxLongSide);
        var identity = !recipe.HasGeometry && width == source.Width && height == source.Height;
        var pixels = identity ? source.Bgra : Resample(source, geometry, width, height, cancellationToken);
        var field = MaskField.Build(source, geometry, mask, invert, cancellationToken);
        var alpha = new byte[width * height];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var v = (y + 0.5) / height;
            for (var x = 0; x < width; x++)
            {
                var i = (y * width) + x;
                alpha[i] = (byte)MathF.Round(field.Alpha((x + 0.5) / width, v, MaskField.Luma(pixels, i * 4)) * 255);
            }
        });

        return new MaskImage(width, height, alpha);
    }

    /// <summary>The local adjustment's settings as a recipe, so it uses the same operations as the global ones.</summary>
    internal static EditRecipe AsRecipe(LocalAdjustment local) => EditRecipe.Neutral with
    {
        Exposure = local.Exposure,
        Temperature = local.Temperature,
        Tint = local.Tint,
        Contrast = local.Contrast,
        Highlights = local.Highlights,
        Shadows = local.Shadows,
        Saturation = local.Saturation,
        Vibrance = local.Vibrance,
    };

    private static List<(ToneOps Ops, MaskField Mask)> LocalsToApply(AnalysisImage source, EditGeometry geometry, EditRecipe recipe, IReadOnlyDictionary<Guid, MaskImage>? masks, CancellationToken cancellationToken)
    {
        var result = new List<(ToneOps, MaskField)>();
        if (masks is null)
        {
            return result;
        }

        foreach (var local in recipe.LocalAdjustments)
        {
            if (!local.HasNoEffect && masks.TryGetValue(local.Id, out var mask))
            {
                result.Add((new ToneOps(AsRecipe(local)), MaskField.Build(source, geometry, mask, local.Mask.Invert, cancellationToken)));
            }
        }

        return result;
    }

    private static byte[] Resample(AnalysisImage source, EditGeometry geometry, int width, int height, CancellationToken cancellationToken)
    {
        var src = source.Bgra;
        int sw = source.Width, sh = source.Height;
        var dst = new byte[width * height * 4];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var v = (y + 0.5) / height;
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var (sx, sy) = geometry.ToSource((x + 0.5) / width, v);

                // Bilinear, pixel centres at +0.5, clamped at the edges.
                var fx = Math.Clamp(sx - 0.5, 0, sw - 1);
                var fy = Math.Clamp(sy - 0.5, 0, sh - 1);
                int x0 = (int)fx, y0 = (int)fy;
                int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                float wx = (float)(fx - x0), wy = (float)(fy - y0);
                int i00 = ((y0 * sw) + x0) * 4, i01 = ((y0 * sw) + x1) * 4, i10 = ((y1 * sw) + x0) * 4, i11 = ((y1 * sw) + x1) * 4;
                var d = row + (x * 4);
                for (var c = 0; c < 3; c++)
                {
                    var top = src[i00 + c] + ((src[i01 + c] - src[i00 + c]) * wx);
                    var bottom = src[i10 + c] + ((src[i11 + c] - src[i10 + c]) * wx);
                    dst[d + c] = (byte)Math.Clamp((int)MathF.Round(top + ((bottom - top) * wy)), 0, 255);
                }

                dst[d + 3] = 255;
            }
        });

        return dst;
    }

    private static void ApplyTone(byte[] pixels, int width, int height, ToneOps ops, List<(ToneOps Ops, MaskField Mask)> locals, CancellationToken cancellationToken)
    {
        var toLinear = ColourMath.ToLinear;
        Parallel.For(0, height, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            var v = (y + 0.5) / height;
            var start = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var i = start + (x * 4);
                var luma = locals.Count == 0 ? 0 : MaskField.Luma(pixels, i);
                float b = toLinear[pixels[i]], g = toLinear[pixels[i + 1]], r = toLinear[pixels[i + 2]];
                ops.Apply(ref r, ref g, ref b);
                foreach (var (localOps, mask) in locals)
                {
                    var alpha = mask.Alpha((x + 0.5) / width, v, luma);
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    float lr = r, lg = g, lb = b;
                    localOps.Apply(ref lr, ref lg, ref lb);
                    r += alpha * (lr - r);
                    g += alpha * (lg - g);
                    b += alpha * (lb - b);
                }

                pixels[i] = ColourMath.ToByte(b);
                pixels[i + 1] = ColourMath.ToByte(g);
                pixels[i + 2] = ColourMath.ToByte(r);
            }
        });
    }
}
