using System.Globalization;
using FrameForge.Application.Ai;
using FrameForge.Core.Editing;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Auto;

/// <summary>
/// Auto crop: frames the photo's main faces at a chosen aspect ratio. Explainable rules, no model beyond the stored
/// face boxes. Options: Tight (upper body), Medium (whole figure), Wide (figure in its setting).
/// Rules:
/// <list type="bullet">
/// <item>Only main faces count (large enough to judge, at least half the largest), so spectators are ignored.</item>
/// <item>Faces are never cut; the face line sits a little above the upper third.</item>
/// <item>A crop that would keep nearly the whole frame is not offered: the photo is already framed loosely.</item>
/// <item>A single subject goes on the nearer vertical third; a group is centred.</item>
/// <item>No crop falls below <see cref="DefaultMinLongSide"/> pixels on its long side, so distant players do not end up soft.</item>
/// </list>
/// </summary>
public static class AutoCrop
{
    public const string Title = "Auto crop";
    public const int DefaultMinLongSide = 2000;

    /// <summary>Shown when every option would keep nearly the whole frame.</summary>
    public const string AlreadyFramed = "The photo is already framed loosely around the subject, so no crop is suggested.";

    /// <summary>
    /// Crop height in face heights (added to the faces' own extent) for Tight, Medium and Wide. Tuned on the user's own
    /// crops (docs/evaluation-m08.md): their crops were a median of about 21 face heights tall, so Medium matches that.
    /// </summary>
    private static readonly (string Name, double FaceHeights, string Framing)[] Variants =
    [
        ("Tight", 12, "upper body"),
        ("Medium", 20, "whole figure"),
        ("Wide", 30, "figure in its setting"),
    ];

    /// <summary>Where the faces' eye line sits, as a share of the crop height from the top (the user's crops: 0.28).</summary>
    private const double EyeLine = 0.28;

    /// <summary>A crop covering at least this share of the frame in both directions is not worth offering.</summary>
    private const double NearlyFull = 0.95;

    /// <param name="sourceWidth">Full-resolution width of the upright photo.</param>
    /// <param name="sourceHeight">Full-resolution height of the upright photo.</param>
    /// <param name="faces">Face boxes normalised to the upright photo (from face analysis).</param>
    /// <param name="pixelRatio">Width ÷ height of the crop in pixels; null keeps the frame's own ratio.</param>
    /// <param name="minFaceScore">Detector score a face needs to count.</param>
    public static AutoSuggestions Suggest(
        int sourceWidth,
        int sourceHeight,
        IReadOnlyList<FaceFinding> faces,
        EditRecipe current,
        double? pixelRatio,
        double minFaceScore,
        int minLongSide = DefaultMinLongSide)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(current);
        var culture = CultureInfo.CurrentCulture;

        var main = AiInsights.MainFaces(faces.Where(f => f.Score >= minFaceScore).ToList());
        if (main.Count == 0)
        {
            return AutoSuggestions.None(Title, faces.Count == 0
                ? "No faces found in this photo, so there is no subject to frame."
                : "Faces were found but none is large enough to be the subject (only small or background faces).");
        }

        var frame = new EditGeometry(sourceWidth, sourceHeight, current with { Crop = CropRect.Full });
        double fw = frame.OutputWidth, fh = frame.OutputHeight;
        var boxes = main.Select(f => FrameBox(frame, f, sourceWidth, sourceHeight)).ToList();
        var union = (Left: boxes.Min(b => b.Left), Top: boxes.Min(b => b.Top), Right: boxes.Max(b => b.Right), Bottom: boxes.Max(b => b.Bottom));
        var faceHeight = boxes.Select(b => b.Bottom - b.Top).OrderBy(v => v).ElementAt(boxes.Count / 2); // median, normalised
        var ratio = pixelRatio ?? (fw / fh);
        var normalisedRatio = ratio / (fw / fh); // width ÷ height in frame-normalised units
        var group = boxes.Count > 1;

        var options = new List<AutoSuggestion>();
        var nearlyFull = false;
        foreach (var (name, faceHeights, framing) in Variants)
        {
            if (Frame(union, faceHeight, faceHeights, normalisedRatio, group, fw, fh, minLongSide) is not { } crop)
            {
                continue;
            }

            if (crop.Width >= NearlyFull && crop.Height >= NearlyFull)
            {
                nearlyFull = true;
                continue;
            }

            // Drop near-duplicates (e.g. Tight grown to the minimum size equals Medium).
            if (options.Any(o => Math.Abs(o.Recipe.Crop.Width - crop.Width) < 0.02 && Math.Abs(o.Recipe.Crop.Height - crop.Height) < 0.02))
            {
                continue;
            }

            var pixels = (W: crop.Width * fw, H: crop.Height * fh);
            var reason = string.Create(culture,
                $"{Capitalise(framing)} around {(group ? $"{boxes.Count} main faces" : "the main face")}, ")
                + (group ? "centred" : "on a third") + " with the faces high in the frame"
                + string.Create(culture, $"; {pixels.W:N0} × {pixels.H:N0} px.");
            options.Add(new AutoSuggestion(name, (current with { Crop = crop }).EnsureValid(), reason));
        }

        return options.Count == 0
            ? AutoSuggestions.None(Title, nearlyFull
                ? AlreadyFramed
                : "The faces cannot be framed at this aspect ratio without cutting one off. Try another ratio.")
            : new AutoSuggestions(Title, options);
    }

    /// <summary>A face box in frame-normalised coordinates (after quarter turns and straighten, before crop).</summary>
    private static (double Left, double Top, double Right, double Bottom) FrameBox(EditGeometry frame, FaceFinding f, int w, int h)
    {
        var corners = new[] { (f.X, f.Y), (f.X + f.Width, f.Y), (f.X, f.Y + f.Height), (f.X + f.Width, f.Y + f.Height) }
            .Select(c => frame.ToOutput(c.Item1 * w, c.Item2 * h))
            .ToList();
        return (corners.Min(c => c.U), corners.Min(c => c.V), corners.Max(c => c.U), corners.Max(c => c.V));
    }

    internal static CropRect? Frame(
        (double Left, double Top, double Right, double Bottom) faces,
        double faceHeight,
        double faceHeights,
        double normalisedRatio,
        bool group,
        double frameWidthPx,
        double frameHeightPx,
        int minLongSide)
    {
        var facesWidth = faces.Right - faces.Left;
        var facesHeight = faces.Bottom - faces.Top;

        // Height from the framing, then width from the ratio; widen if the faces (with breathing room) do not fit.
        var height = facesHeight + (faceHeight * faceHeights);
        var width = height * normalisedRatio;
        var neededWidth = facesWidth + (faceHeight * 2 * (frameHeightPx / frameWidthPx));
        if (width < neededWidth)
        {
            width = neededWidth;
            height = width / normalisedRatio;
        }

        // Never smaller than the minimum output size (unless the frame itself is smaller).
        var longPx = Math.Max(width * frameWidthPx, height * frameHeightPx);
        var targetPx = Math.Min(minLongSide, Math.Max(frameWidthPx, frameHeightPx));
        if (longPx < targetPx)
        {
            var grow = targetPx / longPx;
            width *= grow;
            height *= grow;
        }

        // Fit inside the frame, keeping the ratio.
        var shrink = Math.Min(1, Math.Min(1 / width, 1 / height));
        width *= shrink;
        height *= shrink;
        if (width + 1e-9 < facesWidth || height + 1e-9 < facesHeight)
        {
            return null;
        }

        // Faces' eye line a little above the upper third; single subject on the nearer third, group centred.
        var centreX = (faces.Left + faces.Right) / 2;
        var eyeLine = faces.Top + (facesHeight * 0.45);
        var x = group ? centreX - (width / 2) : centreX < 0.5 ? centreX - (width / 3) : centreX - (2 * width / 3);
        var y = eyeLine - (height * EyeLine);

        // Keep inside the frame, then make sure no face is cut (shift towards it if needed).
        x = Math.Clamp(x, 0, 1 - width);
        y = Math.Clamp(y, 0, 1 - height);
        x = Math.Clamp(x, Math.Max(0, faces.Right - width), Math.Min(faces.Left, 1 - width));
        y = Math.Clamp(y, Math.Max(0, faces.Bottom - height), Math.Min(faces.Top, 1 - height));
        if (faces.Left < x - 1e-9 || faces.Right > x + width + 1e-9 || faces.Top < y - 1e-9 || faces.Bottom > y + height + 1e-9)
        {
            return null;
        }

        return new CropRect(Math.Round(x, 5), Math.Round(y, 5), Math.Round(Math.Min(width, 1 - Math.Round(x, 5)), 5), Math.Round(Math.Min(height, 1 - Math.Round(y, 5)), 5));
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
