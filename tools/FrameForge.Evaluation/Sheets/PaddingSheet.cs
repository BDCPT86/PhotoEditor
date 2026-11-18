using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FrameForge.AI;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Masks;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using FrameForge.Rendering;
using Microsoft.ML.OnnxRuntime;
using MaskCandidates = FrameForge.Evaluation.Masks.MaskCandidates;
using MaskMath = FrameForge.Evaluation.Masks.MaskMath;

namespace FrameForge.Evaluation.Sheets;

/// <summary>Round 3: one photo, two subject masks (padded and unpadded boxes), sides random.</summary>
internal sealed record PaddingItem(
    string Id,
    string File,
    bool PaddedOnLeft,
    string PaddedSummary,
    double PaddedMs,
    double UnpaddedMs,
    double PaddedCoverage,
    double UnpaddedCoverage,
    bool Identical);

/// <summary>
/// Milestone 08 phase B, step B3: checks the box padding on fresh photos (none from rounds 1–2). Each photo is masked
/// twice by the app's own <see cref="SubjectMaskGenerator"/>, once with <see cref="SubjectMaskOptions.Default"/> (padded)
/// and once with <see cref="SubjectMaskOptions.Unpadded"/> (round 2's prompts), and both are graded Good / Usable / Bad
/// without knowing which is which.
/// </summary>
internal static class PaddingSheet
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Build(string modelsDir, string photosDir, string outDir, int count, int seed, IReadOnlySet<string> excludeFiles, StringBuilder report)
    {
        var paths = new[] { MaskCandidates.PersonDetector, MaskCandidates.SamEncoder, MaskCandidates.SamDecoder }.Select(m => MaskCandidates.Find(modelsDir, m)).ToList();
        if (paths.Any(p => p is null))
        {
            Console.Error.WriteLine("mask-sheet round 3 needs object_detection_yolox_2022nov.onnx, mobile_sam.encoder.onnx and mobile_sam.decoder.onnx (exact SHA-256) in --models.");
            return 2;
        }

        using var runtime = new FolderRuntime(paths[0]!, paths[1]!, paths[2]!);
        var padded = new SubjectMaskGenerator(runtime, SubjectMaskOptions.Default);
        var unpadded = new SubjectMaskGenerator(runtime, SubjectMaskOptions.Unpadded);
        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var id = "padding-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        Directory.CreateDirectory(outDir);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var random = new Random(seed);
        var items = new List<PaddingItem>();
        int noPerson = 0, unreadable = 0, excluded = 0;
        var warmedUp = false;

        foreach (var file in JudgingSheets.Sample(Directory.EnumerateFiles(photosDir).Where(IsJpeg), seed))
        {
            if (items.Count >= count)
            {
                break;
            }

            if (excludeFiles.Contains(Path.GetFileName(file)))
            {
                excluded++;
                continue;
            }

            AnalysisImage image, display;
            try
            {
                image = loader.Load(file, MaskService.AnalysisLongSide);
                display = loader.Load(file, JudgingSheets.DisplayLongSide);
            }
            catch (PhotoReadException)
            {
                unreadable++;
                continue;
            }

            if (!warmedUp)
            {
                // The first run of each session includes one-off set-up; keep it out of the timings.
                padded.Generate(image, [], CancellationToken.None);
                warmedUp = true;
            }

            var sw = Stopwatch.StartNew();
            var a = padded.Generate(image, [], CancellationToken.None);
            var paddedMs = sw.Elapsed.TotalMilliseconds;
            if (!a.HasMask)
            {
                noPerson++;
                continue;
            }

            sw.Restart();
            var b = unpadded.Generate(image, [], CancellationToken.None);
            var unpaddedMs = sw.Elapsed.TotalMilliseconds;
            if (!b.HasMask)
            {
                noPerson++;
                continue;
            }

            var itemId = "p" + (items.Count + 1).ToString("000", Culture);
            var paddedOnLeft = random.Next(2) == 0;
            var paddedView = View(display, a.Mask!);
            var unpaddedView = View(display, b.Mask!);
            Save(encoder, display, Path.Combine(img, itemId + "-photo.jpg"));
            Save(encoder, paddedOnLeft ? paddedView : unpaddedView, Path.Combine(img, itemId + "-a.jpg"));
            Save(encoder, paddedOnLeft ? unpaddedView : paddedView, Path.Combine(img, itemId + "-b.jpg"));
            items.Add(new PaddingItem(
                itemId, Path.GetFileName(file), paddedOnLeft, a.Summary, Math.Round(paddedMs, 1), Math.Round(unpaddedMs, 1),
                Math.Round(a.Mask!.Coverage(), 4), Math.Round(b.Mask!.Coverage(), 4), a.Mask.Alpha.AsSpan().SequenceEqual(b.Mask.Alpha)));
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();
        var sheet = new JudgingSheets.SheetFile("mask-padding", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), 0, null, null, Padding: items);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new
        {
            id,
            items = items.Select(i => new { id = i.Id, photo = "img/" + i.Id + "-photo.jpg", left = "img/" + i.Id + "-a.jpg", right = "img/" + i.Id + "-b.jpg" }),
        });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(SheetPages.Mask, pageData));

        report.AppendLine(Culture, $"Padding sheet `{id}`: {items.Count} photos with people (padded vs unpadded boxes, sides random, seed {seed}).");
        report.AppendLine(Culture, $"Skipped {noPerson} photo(s) with no person, {unreadable} unreadable and {excluded} from earlier sheets.");
        report.AppendLine(Culture, $"- The two masks are identical on {items.Count(i => i.Identical)} photo(s).");
        AppendTimings(report, items);
        return 0;
    }

    private static void AppendTimings(StringBuilder report, List<PaddingItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        static double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToList();
            return sorted[sorted.Count / 2];
        }

        report.AppendLine(Culture, $"- Whole subject mask (detect, encode, decode), median: padded {Median(items.Select(i => i.PaddedMs)) / 1000:0.0} s, unpadded {Median(items.Select(i => i.UnpaddedMs)) / 1000:0.0} s.");
        report.AppendLine(Culture, $"- Median share of the photo inside the mask: padded {Median(items.Select(i => i.PaddedCoverage)):P1}, unpadded {Median(items.Select(i => i.UnpaddedCoverage)):P1}.");
    }

    public static int Score(JudgingSheets.SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var items = sheet.Padding ?? [];
        var rows = new List<(PaddingItem Item, MaskGrade Padded, MaskGrade Unpadded)>();
        foreach (var item in items)
        {
            if (!raw.TryGetValue(item.Id, out var answer))
            {
                continue;
            }

            if (answer.Length < 2)
            {
                throw new FormatException("The answer for " + item.Id + " needs a grade for both sides.");
            }

            var left = MaskSheet.ParseGrade(item.Id, answer[0]);
            var right = MaskSheet.ParseGrade(item.Id, answer[1]);
            rows.Add(item.PaddedOnLeft ? (item, left, right) : (item, right, left));
        }

        report.AppendLine(Culture, $"## Subject masks: padded vs unpadded boxes — sheet `{sheet.Id}`");
        report.AppendLine();
        report.AppendLine(Culture, $"- Photos answered: {rows.Count} of {items.Count}");
        foreach (var (name, pick) in new (string, Func<(PaddingItem Item, MaskGrade Padded, MaskGrade Unpadded), MaskGrade>)[] { ("Padded", r => r.Padded), ("Unpadded (round 2)", r => r.Unpadded) })
        {
            var grades = rows.Select(pick).ToList();
            int good = grades.Count(g => g == MaskGrade.Good), usable = grades.Count(g => g == MaskGrade.Usable), bad = grades.Count(g => g == MaskGrade.Bad);
            var (low, high) = SheetScoring.Wilson(good + usable, grades.Count);
            report.AppendLine(Culture, $"- **{name}:** good {good} · usable {usable} · bad {bad}; good or usable {(grades.Count == 0 ? 0 : (good + usable) / (double)grades.Count):P0} (95 % interval {low:P0}–{high:P0})");
        }

        int paddedBetter = rows.Count(r => r.Padded < r.Unpadded), unpaddedBetter = rows.Count(r => r.Unpadded < r.Padded);
        report.AppendLine(Culture, $"- Graded better: padded on {paddedBetter}, unpadded on {unpaddedBetter}, equal on {rows.Count - paddedBetter - unpaddedBetter}; sign test p = {SheetScoring.SignTestP(paddedBetter, unpaddedBetter):0.000}");
        report.AppendLine(Culture, $"- Identical masks (graded on looks alone): {rows.Count(r => r.Item.Identical)}");
        report.AppendLine();
        AppendTimings(report, items);
        report.AppendLine();
        report.AppendLine("| Photo | File | Found | Padded | Unpadded |");
        report.AppendLine("|---|---|---|---|---|");
        foreach (var (item, p, u) in rows)
        {
            report.AppendLine(Culture, $"| {item.Id} | {item.File} | {item.PaddedSummary} | {p} | {u} |");
        }

        report.AppendLine();
        report.AppendLine("Good = would use as is; usable = would use after a small fix; bad = wrong subject, big parts missing or much background included. One person's grades on one set of photos.");
        return 0;
    }

    private static AnalysisImage View(AnalysisImage display, MaskImage mask)
    {
        var alpha = mask.Alpha.Select(a => a / 255f).ToArray();
        return MaskMath.Overlay(display, MaskMath.Resize(alpha, mask.Width, mask.Height, display.Width, display.Height));
    }

    private static bool IsJpeg(string f) => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static void Save(WicJpegEncoder encoder, AnalysisImage image, string path)
    {
        using var stream = File.Create(path);
        encoder.Write(image, null, stream, 90);
    }

    /// <summary>The app's model classes, loaded from verified files in a folder instead of the model store.</summary>
    private sealed class FolderRuntime : IAiRuntime, IDisposable
    {
        private readonly AI.YoloxPersonDetector _detector;
        private readonly MobileSamSegmenter _segmenter;

        public FolderRuntime(string detectorPath, string encoderPath, string decoderPath)
        {
            _detector = new AI.YoloxPersonDetector(Open(detectorPath), AiModelCatalog.PersonDetector);
            _segmenter = new MobileSamSegmenter(Open(encoderPath), Open(decoderPath), AiModelCatalog.SubjectMaskEncoder, AiModelCatalog.SubjectMaskDecoder);
        }

        public string? LastError => null;

        public IFaceDetector? GetFaceDetector() => null;

        public IImageEmbedder? GetImageEmbedder() => null;

        public IPersonDetector? GetPersonDetector() => _detector;

        public IPromptSegmenter? GetPromptSegmenter() => _segmenter;

        public void Reset()
        {
        }

        public void Dispose()
        {
            _detector.Dispose();
            _segmenter.Dispose();
        }

        private static InferenceSession Open(string path) => new(path, MaskCandidates.Options());
    }
}
