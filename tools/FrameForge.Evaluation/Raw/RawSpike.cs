using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.Analysis;
using FrameForge.Imaging;

namespace FrameForge.Evaluation.Raw;

/// <summary>
/// M07 A1: can LibRaw open the user's RAW files, how fast, how much memory, and how close are its colours to the
/// camera's own JPEG? Also feeds damaged copies to LibRaw in a child process, to see whether a bad file can take the
/// app down. RAW files are opened read-only; everything written goes to the output folder.
/// </summary>
internal static class RawSpike
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly string[] RawExtensions = [".cr3", ".cr2", ".dng", ".nef", ".arw", ".raf", ".orf", ".rw2"];
    private static readonly string[] JpegExtensions = [".JPG", ".jpg", ".jpeg", ".JPEG"];

    public static int Run(string rawDir, string? jpegDir, string dllPath, string outDir, int count, StringBuilder report)
    {
        LibRawNative.Load(dllPath);
        Directory.CreateDirectory(outDir);
        var compareDir = Directory.CreateDirectory(Path.Combine(outDir, "compare")).FullName;

        var cameras = LibRawNative.Cameras();
        report.AppendLine(C, $"## RAW decoding with LibRaw {LibRawNative.Version}");
        report.AppendLine();
        report.AppendLine(C, $"- DLL: `{Path.GetFileName(dllPath)}`, SHA-256 {Sha256(dllPath)}");
        report.AppendLine(C, $"- Cameras in LibRaw's list: {cameras.Count}; Canon EOS R50 listed: {(cameras.Any(c => c.Equals("Canon EOS R50", StringComparison.OrdinalIgnoreCase)) ? "yes" : "no")}");

        var files = Directory.EnumerateFiles(rawDir)
            .Where(f => RawExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
        report.AppendLine(C, $"- RAW files: {files.Count} in the folder (first {count} by name), {string.Join(", ", files.GroupBy(f => Path.GetExtension(f).ToUpperInvariant()).Select(g => $"{g.Count()} {g.Key}"))}");
        report.AppendLine(C, $"- Windows' own RAW codec (WIC): {WicStatus(files.FirstOrDefault())}");
        report.AppendLine();
        if (files.Count == 0)
        {
            report.AppendLine("No RAW files found.");
            return 1;
        }

        var pairs = PairWithJpegs(files, jpegDir, report);
        using var memory = new MemorySampler();
        var rows = new List<Row>();
        foreach (var file in files)
        {
            Console.Error.WriteLine("RAW " + Path.GetFileName(file));
            rows.Add(Measure(file, pairs.GetValueOrDefault(file), compareDir, memory));
        }

        WriteResults(rows, report);
        report.AppendLine();
        Corruption(files[0], dllPath, Path.Combine(outDir, "damaged"), report);
        report.AppendLine();
        report.AppendLine(C, $"Side-by-side pictures in `{compareDir}`: camera JPEG | embedded preview | LibRaw, auto brightness | LibRaw, no auto brightness.");
        return 0;
    }

    /// <summary>The child-process half of the damaged-file test: decode one file and say how it went.</summary>
    public static int DecodeOne(string dllPath, string file)
    {
        LibRawNative.Load(dllPath);
        try
        {
            using var raw = RawFile.Open(file);
            var info = raw.Info();
            var preview = raw.Preview();
            var pixels = raw.Develop(cameraWhiteBalance: true, autoBright: true);
            Console.WriteLine(string.Create(C, $"{info.Model}, preview {(preview is null ? "none" : $"{preview.Width}×{preview.Height}")}, image {pixels.Width}×{pixels.Height}"));
            return 0;
        }
        catch (RawDecodeException ex)
        {
            Console.WriteLine(ex.Message);
            return 3;
        }
    }

    private sealed record Row(
        string File, string Camera, string RawSize, int Flip, double OpenMs,
        string Preview, double PreviewMs, double PreviewDecodeMs, double PreviewThumbMs,
        string Image, double DevelopMs, double DevelopFlatMs, double PeakMb,
        Colour? PreviewVsJpeg, Colour? DevelopVsJpeg, Colour? FlatVsJpeg, string? Error);

    /// <summary>How far one image's colours are from the camera JPEG's (CIE76 ΔE in Lab, at 200 px).</summary>
    private sealed record Colour(double GlobalDeltaE, double MeanDeltaE, double Lightness);

    /// <summary>
    /// The camera JPEG for each RAW file: one with the same name next to it, otherwise (with <paramref name="jpegDir"/>)
    /// the one in that folder taken in the same second. Two JPEGs in the same second are ambiguous and not used.
    /// </summary>
    private static Dictionary<string, string> PairWithJpegs(List<string> files, string? jpegDir, StringBuilder report)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ILookup<DateTime, string>? byTime = null;
        if (jpegDir is not null)
        {
            var reader = new WicPhotoMetadataReader();
            byTime = Directory.EnumerateFiles(jpegDir)
                .Where(f => JpegExtensions.Contains(Path.GetExtension(f)))
                .Select(f => (File: f, Taken: TryTaken(reader, f)))
                .Where(x => x.Taken is not null)
                .ToLookup(x => x.Taken!.Value, x => x.File);
        }

        var ambiguous = 0;
        foreach (var file in files)
        {
            if (JpegExtensions.Select(e => Path.ChangeExtension(file, e)).FirstOrDefault(File.Exists) is { } sibling)
            {
                pairs[file] = sibling;
                continue;
            }

            if (byTime is null)
            {
                continue;
            }

            DateTime? taken;
            try
            {
                using var raw = RawFile.Open(file);
                taken = raw.Info().Taken?.LocalDateTime; // LibRaw turned the EXIF local time into Unix time with this PC's zone
            }
            catch (RawDecodeException)
            {
                continue;
            }

            if (taken is { } t)
            {
                var matches = byTime[new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Unspecified)].ToList();
                if (matches.Count == 1)
                {
                    pairs[file] = matches[0];
                }
                else if (matches.Count > 1)
                {
                    ambiguous++;
                }
            }
        }

        if (jpegDir is not null)
        {
            report.AppendLine(C, $"- Camera JPEGs from `{Path.GetFileName(jpegDir.TrimEnd('\\', '/'))}`, paired by capture time: {pairs.Count} of {files.Count} RAW files{(ambiguous > 0 ? $" ({ambiguous} skipped: several JPEGs in the same second)" : string.Empty)}");
            foreach (var (raw, jpeg) in pairs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.AppendLine(C, $"  - {Path.GetFileName(raw)} ↔ {Path.GetFileName(jpeg)}");
            }

            report.AppendLine();
        }

        return pairs;
    }

    private static DateTime? TryTaken(WicPhotoMetadataReader reader, string path)
    {
        try
        {
            return reader.Read(path).CaptureLocalTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Row Measure(string file, string? jpegPath, string compareDir, MemorySampler memory)
    {
        var name = Path.GetFileName(file);
        var cameraJpeg = jpegPath is null ? null : new WicAnalysisImageLoader().Load(jpegPath, 480);
        try
        {
            memory.Reset();
            var sw = Stopwatch.StartNew();
            using var raw = RawFile.Open(file);
            var info = raw.Info();
            var openMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var preview = raw.Preview();
            var previewMs = sw.Elapsed.TotalMilliseconds;
            AnalysisImage? previewImage = null;
            double decodeMs = 0, thumbMs = 0;
            if (preview is { Format: "JPEG" })
            {
                sw.Restart();
                previewImage = Orient(DecodeJpeg(preview.Data, null), info.Flip);
                decodeMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                _ = DecodeJpeg(preview.Data, 400);
                thumbMs = sw.Elapsed.TotalMilliseconds;
            }

            sw.Restart();
            var developed = raw.Develop(cameraWhiteBalance: true, autoBright: true);
            var developMs = sw.Elapsed.TotalMilliseconds;
            var peak = memory.PeakMb;
            var developedImage = ToImage(developed);

            // A fresh handle for the second setting, so the first run's state can't leak into it.
            sw.Restart();
            RawPixels flat;
            using (var again = RawFile.Open(file))
            {
                flat = again.Develop(cameraWhiteBalance: true, autoBright: false);
            }

            var flatMs = sw.Elapsed.TotalMilliseconds;
            var flatImage = ToImage(flat);

            var small = new[] { cameraJpeg, previewImage is null ? null : Scale(previewImage, 480), Scale(developedImage, 480), Scale(flatImage, 480) };
            WriteStrip(small, Path.Combine(compareDir, Path.GetFileNameWithoutExtension(file) + ".jpg"));

            return new Row(
                name, $"{info.Make} {info.Model}".Trim(), $"{info.RawWidth}×{info.RawHeight}", info.Flip, openMs,
                preview is null ? "none" : previewImage is not null ? $"{preview.Format} {previewImage.Width}×{previewImage.Height}" : $"{preview.Format} {preview.Width}×{preview.Height}", previewMs, decodeMs, thumbMs,
                $"{developed.Width}×{developed.Height}", developMs, flatMs, peak,
                Compare(previewImage, cameraJpeg), Compare(developedImage, cameraJpeg), Compare(flatImage, cameraJpeg), null);
        }
        catch (RawDecodeException ex)
        {
            return new Row(name, "", "", 0, 0, "", 0, 0, 0, "", 0, 0, 0, null, null, null, ex.Message);
        }
    }

    private static void WriteResults(List<Row> rows, StringBuilder report)
    {
        var ok = rows.Where(r => r.Error is null).ToList();
        report.AppendLine(C, $"### {ok.Count} of {rows.Count} files decoded");
        report.AppendLine();
        foreach (var failed in rows.Where(r => r.Error is not null))
        {
            report.AppendLine(C, $"- **{failed.File}:** {failed.Error}");
        }

        if (ok.Count == 0)
        {
            return;
        }

        report.AppendLine(C, $"- Cameras: {string.Join(", ", ok.Select(r => r.Camera).Distinct())}; RAW size {string.Join(", ", ok.Select(r => r.RawSize).Distinct())}; developed size {string.Join(", ", ok.Select(r => r.Image).Distinct())}");
        report.AppendLine(C, $"- Embedded previews: {string.Join(", ", ok.GroupBy(r => r.Preview).Select(g => $"{g.Count()} × {g.Key}"))}");
        report.AppendLine(C, $"- Orientation values (LibRaw flip): {string.Join(", ", ok.GroupBy(r => r.Flip).Select(g => $"{g.Key} on {g.Count()}"))}");
        report.AppendLine();
        report.AppendLine("Median (90th percentile) in ms:");
        report.AppendLine();
        report.AppendLine("| Stage | ms |");
        report.AppendLine("|---|---:|");
        Line("Open and read the header", ok.Select(r => r.OpenMs));
        Line("Extract the embedded preview", ok.Select(r => r.PreviewMs));
        Line("Decode the preview at full size (viewer)", ok.Where(r => r.PreviewDecodeMs > 0).Select(r => r.PreviewDecodeMs));
        Line("Decode the preview at 400 px (thumbnail)", ok.Where(r => r.PreviewThumbMs > 0).Select(r => r.PreviewThumbMs));
        Line("**Develop** (unpack, demosaic, 8-bit sRGB)", ok.Select(r => r.DevelopMs));
        Line("Develop again, no auto brightness (fresh handle)", ok.Select(r => r.DevelopFlatMs));
        report.AppendLine();
        report.AppendLine(C, $"- Memory: the process working set peaked at {ok.Max(r => r.PeakMb):0} MB during a file (median {Median(ok.Select(r => r.PeakMb)):0} MB).");

        var paired = ok.Where(r => r.DevelopVsJpeg is not null).ToList();
        report.AppendLine();
        if (paired.Count == 0)
        {
            report.AppendLine("No RAW+JPEG pairs, so colours were not compared with the camera's JPEG.");
        }
        else
        {
            report.AppendLine(C, $"### Colour against the camera's JPEG ({paired.Count} pairs)");
            report.AppendLine();
            report.AppendLine("ΔE is CIE76 in Lab at 200 px: under about 2 is hard to see, 5–10 is a clear shift. Global = the difference of the average colours (a cast); mean = per pixel (also counts small misalignment). Lightness = L* difference, negative means darker than the camera JPEG.");
            report.AppendLine();
            report.AppendLine("| Image | Global ΔE, median (max) | Mean ΔE, median | Lightness, median |");
            report.AppendLine("|---|---:|---:|---:|");
            ColourLine("Embedded preview", paired.Select(r => r.PreviewVsJpeg));
            ColourLine("LibRaw, auto brightness", paired.Select(r => r.DevelopVsJpeg));
            ColourLine("LibRaw, no auto brightness", paired.Select(r => r.FlatVsJpeg));
        }

        report.AppendLine();
        report.AppendLine("| File | Camera | Flip | Preview | Develop ms | Peak MB | ΔE preview | ΔE LibRaw (auto) | ΔE LibRaw (flat) |");
        report.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|");
        foreach (var r in ok)
        {
            report.AppendLine(C, $"| {r.File} | {r.Camera} | {r.Flip} | {r.Preview} | {r.DevelopMs:0} | {r.PeakMb:0} | {Fmt(r.PreviewVsJpeg)} | {Fmt(r.DevelopVsJpeg)} | {Fmt(r.FlatVsJpeg)} |");
        }

        void Line(string label, IEnumerable<double> values)
        {
            var list = values.ToList();
            if (list.Count > 0)
            {
                report.AppendLine(C, $"| {label} | {Median(list):0} ({Percentile(list, 0.9):0}) |");
            }
        }

        void ColourLine(string label, IEnumerable<Colour?> values)
        {
            var list = values.OfType<Colour>().ToList();
            if (list.Count > 0)
            {
                report.AppendLine(C, $"| {label} | {Median(list.Select(c => c.GlobalDeltaE)):0.0} ({list.Max(c => c.GlobalDeltaE):0.0}) | {Median(list.Select(c => c.MeanDeltaE)):0.0} | {Median(list.Select(c => c.Lightness)):+0.0;-0.0} |");
            }
        }

        static string Fmt(Colour? c) => c is null ? "–" : string.Create(C, $"{c.GlobalDeltaE:0.0}");
    }

    /// <summary>
    /// Damaged copies of one file, each decoded in a child process: a native crash ends only the child, and its exit
    /// code says what happened.
    /// </summary>
    private static void Corruption(string source, string dllPath, string dir, StringBuilder report)
    {
        Directory.CreateDirectory(dir);
        var bytes = File.ReadAllBytes(source);
        var ext = Path.GetExtension(source);
        var random = new Random(7);
        var cases = new List<(string Label, byte[] Data)>
        {
            ("empty file", []),
            ("first 1 % only", bytes[..(bytes.Length / 100)]),
            ("first 10 % only", bytes[..(bytes.Length / 10)]),
            ("first 50 % only", bytes[..(bytes.Length / 2)]),
            ("first 90 % only", bytes[..(bytes.Length * 9 / 10)]),
            ("first 4 KB zeroed", Zeroed(bytes, 0, 4096)),
            ("4 KB zeroed at 1 %", Zeroed(bytes, bytes.Length / 100, 4096)),
            ("1,000 random bytes changed", Scrambled(bytes, random, 1000)),
            ("10,000 random bytes changed", Scrambled(bytes, random, 10000)),
            ("random noise, same size", Noise(bytes.Length, random)),
        };

        report.AppendLine(C, $"### Damaged files (copies of {Path.GetFileName(source)}, each in a child process)");
        report.AppendLine();
        report.AppendLine("| Damage | Result | Exit code | ms |");
        report.AppendLine("|---|---|---:|---:|");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        for (var i = 0; i < cases.Count; i++)
        {
            var path = Path.Combine(dir, $"damaged-{i:00}{ext}");
            File.WriteAllBytes(path, cases[i].Data);
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "raw-decode-one", "--libraw", dllPath, "--file", path })
            {
                start.ArgumentList.Add(a);
            }

            var sw = Stopwatch.StartNew();
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the child process.");
            var output = child.StandardOutput.ReadToEndAsync();
            _ = child.StandardError.ReadToEndAsync();
            string result;
            if (!child.WaitForExit(120_000))
            {
                child.Kill(entireProcessTree: true);
                result = "**hung** (killed after 2 min)";
            }
            else
            {
                var line = output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;
                result = child.ExitCode switch
                {
                    0 => "decoded anyway: " + line,
                    3 => "refused cleanly: " + line,
                    _ => $"**crashed** (0x{child.ExitCode:X8})",
                };
            }

            report.AppendLine(C, $"| {cases[i].Label} | {result.Replace("|", "/", StringComparison.Ordinal)} | {(child.HasExited ? child.ExitCode : -1)} | {sw.Elapsed.TotalMilliseconds:0} |");
        }

        static byte[] Zeroed(byte[] b, int at, int length)
        {
            var copy = (byte[])b.Clone();
            Array.Clear(copy, at, Math.Min(length, copy.Length - at));
            return copy;
        }

        static byte[] Scrambled(byte[] b, Random random, int n)
        {
            var copy = (byte[])b.Clone();
            for (var i = 0; i < n; i++)
            {
                copy[random.Next(copy.Length)] = (byte)random.Next(256);
            }

            return copy;
        }

        static byte[] Noise(int length, Random random)
        {
            var b = new byte[length];
            random.NextBytes(b);
            return b;
        }
    }

    private static string WicStatus(string? file)
    {
        if (file is null)
        {
            return "not tried";
        }

        try
        {
            using var stream = File.OpenRead(file);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return string.Create(C, $"decodes it ({decoder.CodecInfo?.FriendlyName}, {frame.PixelWidth}×{frame.PixelHeight})");
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or System.Runtime.InteropServices.ExternalException)
        {
            return "no codec installed (" + ex.GetType().Name + ")";
        }
    }

    private static AnalysisImage DecodeJpeg(byte[] data, int? maxWidth)
    {
        using var stream = new MemoryStream(data, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (maxWidth is { } w)
        {
            image.DecodePixelWidth = w;
        }

        image.EndInit();
        image.Freeze();
        return BitmapConversions.ToBuffer(image);
    }

    /// <summary>LibRaw's flip: 0 none, 3 = 180°, 5 = 90° anticlockwise, 6 = 90° clockwise.</summary>
    private static AnalysisImage Orient(AnalysisImage image, int flip)
    {
        var angle = flip switch { 3 => 180, 5 => 270, 6 => 90, _ => 0 };
        if (angle == 0)
        {
            return image;
        }

        var rotated = new TransformedBitmap(BitmapConversions.ToBitmap(image), new RotateTransform(angle));
        return BitmapConversions.ToBuffer(rotated);
    }

    private static AnalysisImage ToImage(RawPixels pixels)
    {
        var bgra = new byte[pixels.Width * pixels.Height * 4];
        for (int i = 0, o = 0; i < pixels.Rgb.Length; i += 3, o += 4)
        {
            bgra[o] = pixels.Rgb[i + 2];
            bgra[o + 1] = pixels.Rgb[i + 1];
            bgra[o + 2] = pixels.Rgb[i];
            bgra[o + 3] = 255;
        }

        return new AnalysisImage(pixels.Width, pixels.Height, bgra);
    }

    private static AnalysisImage Scale(AnalysisImage image, int longSide)
    {
        var s = (double)longSide / Math.Max(image.Width, image.Height);
        if (s >= 1)
        {
            return image;
        }

        var scaled = new TransformedBitmap(BitmapConversions.ToBitmap(image), new ScaleTransform(s, s));
        return BitmapConversions.ToBuffer(scaled);
    }

    private static Colour? Compare(AnalysisImage? image, AnalysisImage? reference)
    {
        if (image is null || reference is null)
        {
            return null;
        }

        var a = Resize(image, 200, 133);
        var b = Resize(reference, 200, 133);
        if ((image.Width > image.Height) != (reference.Width > reference.Height))
        {
            return new Colour(double.NaN, double.NaN, double.NaN); // orientation differs: not comparable
        }

        double sum = 0, la = 0, aa = 0, ba = 0, lb = 0, ab = 0, bb = 0;
        var n = a.Width * a.Height;
        for (var p = 0; p < n * 4; p += 4)
        {
            var x = Lab(a.Bgra[p + 2], a.Bgra[p + 1], a.Bgra[p]);
            var y = Lab(b.Bgra[p + 2], b.Bgra[p + 1], b.Bgra[p]);
            sum += Math.Sqrt(Sq(x.L - y.L) + Sq(x.A - y.A) + Sq(x.B - y.B));
            la += x.L; aa += x.A; ba += x.B; lb += y.L; ab += y.A; bb += y.B;
        }

        la /= n; aa /= n; ba /= n; lb /= n; ab /= n; bb /= n;
        return new Colour(Math.Sqrt(Sq(la - lb) + Sq(aa - ab) + Sq(ba - bb)), sum / n, la - lb);

        static double Sq(double v) => v * v;
    }

    /// <summary>Resizes to an exact size (landscape or portrait as the image is), ignoring a small aspect difference.</summary>
    private static AnalysisImage Resize(AnalysisImage image, int longSide, int shortSide)
    {
        var (w, h) = image.Width >= image.Height ? (longSide, shortSide) : (shortSide, longSide);
        var scaled = new TransformedBitmap(BitmapConversions.ToBitmap(image), new ScaleTransform((double)w / image.Width, (double)h / image.Height));
        var buffer = BitmapConversions.ToBuffer(scaled);
        return buffer.Width == w && buffer.Height == h ? buffer : Crop(buffer, w, h);
    }

    private static AnalysisImage Crop(AnalysisImage image, int w, int h)
    {
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(y, image.Height - 1);
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(x, image.Width - 1);
                Array.Copy(image.Bgra, ((sy * image.Width) + sx) * 4, bgra, ((y * w) + x) * 4, 4);
            }
        }

        return new AnalysisImage(w, h, bgra);
    }

    private static (double L, double A, double B) Lab(byte r, byte g, byte b)
    {
        static double Lin(byte v)
        {
            var c = v / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : (7.787 * t) + (16d / 116);
        var (rl, gl, bl) = (Lin(r), Lin(g), Lin(b));
        var x = ((0.4124 * rl) + (0.3576 * gl) + (0.1805 * bl)) / 0.95047;
        var y = (0.2126 * rl) + (0.7152 * gl) + (0.0722 * bl);
        var z = ((0.0193 * rl) + (0.1192 * gl) + (0.9505 * bl)) / 1.08883;
        var (fx, fy, fz) = (F(x), F(y), F(z));
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static void WriteStrip(AnalysisImage?[] images, string path)
    {
        const int gap = 8;
        var present = images.Select(i => i ?? new AnalysisImage(1, 1, [40, 40, 40, 255])).ToList();
        var height = present.Max(i => i.Height);
        var width = present.Sum(i => i.Width) + (gap * (present.Count - 1));
        var bgra = new byte[width * height * 4];
        Array.Fill(bgra, (byte)24);
        var left = 0;
        foreach (var image in present)
        {
            for (var y = 0; y < image.Height; y++)
            {
                Array.Copy(image.Bgra, y * image.Width * 4, bgra, ((y * width) + left) * 4, image.Width * 4);
            }

            left += image.Width + gap;
        }

        using var file = File.Create(path);
        new WicJpegEncoder().Write(new AnalysisImage(width, height, bgra), FrameForge.Application.Editing.JpegMetadata.Nothing, file, 88);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static double Median(IEnumerable<double> values) => Percentile(values.ToList(), 0.5);

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).Order().ToList();
        return sorted.Count == 0 ? double.NaN : sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
