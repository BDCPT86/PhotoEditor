using FrameForge.Core.Editing;

namespace FrameForge.App.ViewModels;

public enum CropAspect
{
    Free,
    Original,
    Square,
    Portrait4x5,
    Classic3x2,
    Wide16x9,
}

/// <summary>Crop rectangle helpers in normalised frame coordinates (0–1 on each axis).</summary>
internal static class CropMath
{
    /// <summary>Width÷height in pixels for an aspect choice; null for Free. "Original" is the frame's own ratio.</summary>
    public static double? PixelRatio(CropAspect aspect, double frameRatio) => aspect switch
    {
        CropAspect.Original => frameRatio,
        CropAspect.Square => 1,
        CropAspect.Portrait4x5 => 4.0 / 5,
        CropAspect.Classic3x2 => frameRatio >= 1 ? 3.0 / 2 : 2.0 / 3, // follows the photo's orientation
        CropAspect.Wide16x9 => frameRatio >= 1 ? 16.0 / 9 : 9.0 / 16,
        _ => null,
    };

    /// <summary>
    /// The largest crop with pixel ratio <paramref name="pixelRatio"/>, centred on <paramref name="around"/>'s centre
    /// and kept inside the frame. In normalised units the ratio is pixelRatio ÷ frameRatio.
    /// </summary>
    public static CropRect Fit(double pixelRatio, double frameRatio, CropRect around)
    {
        var normalised = pixelRatio / frameRatio;
        double w = 1, h = 1;
        if (normalised >= 1)
        {
            h = 1 / normalised;
        }
        else
        {
            w = normalised;
        }

        var cx = around.X + (around.Width / 2);
        var cy = around.Y + (around.Height / 2);
        var x = Math.Clamp(cx - (w / 2), 0, 1 - w);
        var y = Math.Clamp(cy - (h / 2), 0, 1 - h);
        return new CropRect(x, y, w, h);
    }

    /// <summary>Clamps a rectangle into the frame with a minimum side, keeping its position where possible.</summary>
    public static CropRect Clamp(double x, double y, double width, double height)
    {
        width = Math.Clamp(width, CropRect.MinSide, 1);
        height = Math.Clamp(height, CropRect.MinSide, 1);
        x = Math.Clamp(x, 0, 1 - width);
        y = Math.Clamp(y, 0, 1 - height);
        return new CropRect(Round(x), Round(y), Round(width), Round(height));
    }

    /// <summary>Six decimals: well below a pixel, and keeps stored recipes tidy.</summary>
    private static double Round(double v) => Math.Round(v, 6);
}
