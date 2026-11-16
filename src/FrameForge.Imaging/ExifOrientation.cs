using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameForge.Imaging;

/// <summary>Applies the EXIF orientation tag (1–8) so images display upright.</summary>
public static class ExifOrientation
{
    public static bool IsValid(int? orientation) => orientation is >= 1 and <= 8;

    /// <summary>True when the orientation swaps width and height (90°/270° rotations).</summary>
    public static bool SwapsDimensions(int? orientation) => orientation is >= 5 and <= 8;

    public static BitmapSource Apply(BitmapSource source, int? orientation)
    {
        ArgumentNullException.ThrowIfNull(source);

        var transform = CreateTransform(orientation);
        return transform is null ? source : new TransformedBitmap(source, transform);
    }

    internal static Transform? CreateTransform(int? orientation)
    {
        // TransformGroup applies children in order. Only orthogonal transforms are used,
        // which TransformedBitmap supports.
        static Transform Group(params Transform[] transforms)
        {
            var group = new TransformGroup();
            foreach (var t in transforms)
            {
                group.Children.Add(t);
            }

            return group;
        }

        return orientation switch
        {
            2 => new ScaleTransform(-1, 1),                                  // mirror horizontal
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),                                  // mirror vertical
            5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),  // transpose
            6 => new RotateTransform(90),                                    // rotate 90° CW
            7 => Group(new RotateTransform(270), new ScaleTransform(-1, 1)), // transverse
            8 => new RotateTransform(270),                                   // rotate 90° CCW
            _ => null,                                                       // 1, absent, or invalid
        };
    }
}
