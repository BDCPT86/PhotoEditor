using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameForge.Evaluation.Brand;

/// <summary>
/// Renders the app's icon and splash image from the logo in <c>Themes/Brand.xaml</c>, so they always match the mark
/// the app draws. Output: <c>FrameForge.ico</c> (16–256 px, PNG entries), <c>Splash.png</c>, and <c>Mark-256.png</c>
/// for documents. WPF rendering needs an STA thread, so the work runs on one.
/// </summary>
internal static class BrandAssets
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    public static int Run(string brandXaml, string outDir, StringBuilder report)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Render(brandXaml, outDir, report);
            }
#pragma warning disable CA1031 // reported to the caller on the original thread
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }

        return 0;
    }

    private static void Render(string brandXaml, string outDir, StringBuilder report)
    {
        ResourceDictionary brand;
        using (var stream = File.OpenRead(brandXaml))
        {
            brand = (ResourceDictionary)XamlReader.Load(stream);
        }

        var mark = (DrawingImage)brand["Brand.Mark"];
        Directory.CreateDirectory(outDir);

        // Icon: one PNG per size, packed into an .ico (Vista+ format).
        var entries = IconSizes.Select(size => (size, Png(Draw(size, size, dc => dc.DrawImage(mark, new Rect(0, 0, size, size)))))).ToList();
        var icoPath = Path.Combine(outDir, "FrameForge.ico");
        File.WriteAllBytes(icoPath, Ico(entries));
        report.AppendLine(CultureInfo.InvariantCulture, $"- `{icoPath}`: {string.Join(", ", IconSizes)} px");

        var markPath = Path.Combine(outDir, "Mark-256.png");
        File.WriteAllBytes(markPath, entries.Single(e => e.size == 256).Item2);
        report.AppendLine(CultureInfo.InvariantCulture, $"- `{markPath}`");

        var splashPath = Path.Combine(outDir, "Splash.png");
        File.WriteAllBytes(splashPath, Png(Splash(mark)));
        report.AppendLine(CultureInfo.InvariantCulture, $"- `{splashPath}`: {SplashWidth * SplashScale}×{SplashHeight * SplashScale} px");
    }

    private const int SplashWidth = 480;
    private const int SplashHeight = 200;

    /// <summary>
    /// Windows shows the splash pixel for pixel, without display scaling, so it is drawn at 1.5×: the intended size at 150 %
    /// scaling (common on laptops), still a reasonable size at 100 %.
    /// </summary>
    private const double SplashScale = 1.5;

    /// <summary>
    /// The splash: a dark card with the mark and the name, shown by Windows the moment the app starts and closed as soon
    /// as the main window appears. Colours and type follow docs/ui-design-system.md.
    /// </summary>
    private static RenderTargetBitmap Splash(DrawingImage mark) => Draw((int)(SplashWidth * SplashScale), (int)(SplashHeight * SplashScale), dc =>
    {
        dc.PushTransform(new ScaleTransform(SplashScale, SplashScale));
        var card = new Rect(0.5, 0.5, SplashWidth - 1, SplashHeight - 1);
        dc.DrawRoundedRectangle(Brush("#FF121212"), new Pen(Brush("#FF292929"), 1), card, 12, 12);
        dc.DrawImage(mark, new Rect(40, 52, 96, 96));

        var display = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var text = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var name = Text("FrameForge", display, 34, "#FFECECEC");
        var line = Text("Cull and edit, on your own computer", text, 14, "#FFA6A6A6");
        var x = 40 + 96 + 28;
        var top = (SplashHeight - (name.Height + 6 + line.Height)) / 2;
        dc.DrawText(name, new Point(x, top));
        dc.DrawText(line, new Point(x + 2, top + name.Height + 6));
        dc.Pop();
    });

    private static FormattedText Text(string value, Typeface typeface, double size, string colour) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brush(colour), 1.0);

    private static SolidColorBrush Brush(string colour)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
        brush.Freeze();
        return brush;
    }

    private static RenderTargetBitmap Draw(int width, int height, Action<DrawingContext> paint)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var dc = visual.RenderOpen())
        {
            paint(dc);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico holding PNG images (ICONDIR, one ICONDIRENTRY per size, then the PNG data).</summary>
    private static byte[] Ico(List<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // colour planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
