using FrameForge.Core.Editing;

namespace FrameForge.Rendering;

/// <summary>
/// Maps output pixels back to source pixels for a recipe's quarter turns, straighten and crop. Works in normalised
/// terms, so the same recipe gives the same framing at any source resolution (preview or full size).
/// </summary>
public sealed class EditGeometry
{
    private readonly double _sourceWidth;
    private readonly double _sourceHeight;
    private readonly int _quarterTurns;
    private readonly double _cos;
    private readonly double _sin;
    private readonly double _frameWidth;
    private readonly double _frameHeight;
    private readonly double _innerScale;
    private readonly CropRect _crop;

    public EditGeometry(int sourceWidth, int sourceHeight, EditRecipe recipe)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceHeight, 1);
        ArgumentNullException.ThrowIfNull(recipe);

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        _quarterTurns = ((recipe.QuarterTurns % 4) + 4) % 4;
        (_frameWidth, _frameHeight) = _quarterTurns % 2 == 1 ? (_sourceHeight, _sourceWidth) : (_sourceWidth, _sourceHeight);
        var angle = recipe.Straighten * Math.PI / 180;
        _cos = Math.Cos(angle);
        _sin = Math.Sin(angle);
        _innerScale = InscribedScale(_frameWidth, _frameHeight, Math.Abs(angle));
        _crop = recipe.Crop;

        OutputWidth = _crop.Width * _innerScale * _frameWidth;
        OutputHeight = _crop.Height * _innerScale * _frameHeight;
    }

    /// <summary>Width of the edited picture, in source pixels (before any output resize).</summary>
    public double OutputWidth { get; }

    public double OutputHeight { get; }

    /// <summary>Width/height of the frame after quarter turns, before straighten and crop.</summary>
    public (double Width, double Height) Frame => (_frameWidth, _frameHeight);

    /// <summary>
    /// Scale (0–1] of the largest rectangle with the frame's aspect ratio that fits inside the frame rotated by
    /// <paramref name="angle"/> radians. Its corners must stay inside the rotated frame:
    /// s·(w·cos a + h·sin a) ≤ w and s·(w·sin a + h·cos a) ≤ h.
    /// </summary>
    public static double InscribedScale(double width, double height, double angle)
    {
        var a = Math.Abs(angle);
        double cos = Math.Cos(a), sin = Math.Sin(a);
        return Math.Min(1, Math.Min(width / ((width * cos) + (height * sin)), height / ((width * sin) + (height * cos))));
    }

    /// <summary>Output pixel size for an optional limit on the longest side (never upscales).</summary>
    public (int Width, int Height) OutputSize(int? maxLongSide)
    {
        var scale = maxLongSide is { } max ? Math.Min(1, max / Math.Max(OutputWidth, OutputHeight)) : 1;
        return (Math.Max(1, (int)Math.Round(OutputWidth * scale)), Math.Max(1, (int)Math.Round(OutputHeight * scale)));
    }

    /// <summary>
    /// The source position (continuous pixel coordinates, pixel centres at +0.5) shown at normalised output position
    /// (u, v), each 0–1.
    /// </summary>
    public (double X, double Y) ToSource(double u, double v)
    {
        // In the straightened frame, relative to its centre.
        var dx = (_crop.X + (u * _crop.Width) - 0.5) * _innerScale * _frameWidth;
        var dy = (_crop.Y + (v * _crop.Height) - 0.5) * _innerScale * _frameHeight;

        // The picture is the frame rotated clockwise by the straighten angle (y points down); undo that rotation.
        var x1 = (_frameWidth / 2) + (dx * _cos) + (dy * _sin);
        var y1 = (_frameHeight / 2) - (dx * _sin) + (dy * _cos);

        // Undo the clockwise quarter turns.
        return _quarterTurns switch
        {
            1 => (y1, _sourceHeight - x1),
            2 => (_sourceWidth - x1, _sourceHeight - y1),
            3 => (_sourceWidth - y1, x1),
            _ => (x1, y1),
        };
    }

    /// <summary>
    /// The inverse of <see cref="ToSource"/>: where a source position (continuous pixel coordinates) appears in the
    /// output, as normalised (u, v). Values outside 0–1 mean the point is cropped away. Used to place face boxes on an
    /// edited photo.
    /// </summary>
    public (double U, double V) ToOutput(double sourceX, double sourceY)
    {
        // Apply the clockwise quarter turns (the inverse of ToSource's last step).
        var (x1, y1) = _quarterTurns switch
        {
            1 => (_sourceHeight - sourceY, sourceX),
            2 => (_sourceWidth - sourceX, _sourceHeight - sourceY),
            3 => (sourceY, _sourceWidth - sourceX),
            _ => (sourceX, sourceY),
        };

        // Rotate clockwise by the straighten angle about the frame centre.
        var x = x1 - (_frameWidth / 2);
        var y = y1 - (_frameHeight / 2);
        var dx = (x * _cos) - (y * _sin);
        var dy = (x * _sin) + (y * _cos);

        // Into the inscribed rectangle, then the crop.
        var fu = (dx / (_innerScale * _frameWidth)) + 0.5;
        var fv = (dy / (_innerScale * _frameHeight)) + 0.5;
        return ((fu - _crop.X) / _crop.Width, (fv - _crop.Y) / _crop.Height);
    }

    /// <summary>
    /// Longest side to decode the original at so that rendering to <paramref name="maxOutputLongSide"/> needs no
    /// further downscaling (decoders downscale with better filtering than bilinear sampling). Null = full size.
    /// </summary>
    public static int? DecodeLongSide(int originalWidth, int originalHeight, EditRecipe recipe, int? maxOutputLongSide)
    {
        if (maxOutputLongSide is not { } max)
        {
            return null;
        }

        var full = new EditGeometry(originalWidth, originalHeight, recipe);
        var factor = max / Math.Max(full.OutputWidth, full.OutputHeight);
        return factor >= 1 ? null : (int)Math.Ceiling(Math.Max(originalWidth, originalHeight) * factor);
    }
}
