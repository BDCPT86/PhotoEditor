using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Photos;
using FrameForge.Evaluation.Masks;
using FrameForge.Imaging;

namespace FrameForge.Evaluation.Sheets;

/// <summary>What a mask sheet stores per photo: which side shows MobileSAM, and the measurements (never shown on the page).</summary>
internal sealed record MaskItem(
    string Id,
    string File,
    bool SamOnLeft,
    int MainFaces,
    double SamEncodeMs,
    double SamDecodeMs,
    double ModnetMs,
    double SamCoverage,
    double ModnetCoverage);

/// <summary>Round 2: one MobileSAM mask per photo, prompted by the main people the person detector found.</summary>
internal sealed record PersonMaskItem(
    string Id,
    string File,
    int People,
    int MainPeople,
    double DetectMs,
    double EncodeMs,
    double DecodeMs,
    double Coverage);

internal enum MaskGrade
{
    Good,
    Usable,
    Bad,
}

/// <summary>
/// Milestone 08 phase B, step B1: the subject-mask judging sheet. Each photo with a main face is shown twice, with
/// MobileSAM's and MODNet's mask as an overlay, sides random, and each mask is graded Good / Usable / Bad. Timings and
/// memory are measured while the sheet is built.
/// </summary>
internal static class MaskSheet
{
    /// <summary>Masks are computed at the analysis size used for faces, as they would be in the app.</summary>
    public const int MaskLongSide = 1024;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Build(string modelsDir, string photosDir, string outDir, int count, int seed, IFaceDetector detector, StringBuilder report)
    {
        var encoderPath = MaskCandidates.Find(modelsDir, MaskCandidates.SamEncoder);
        var decoderPath = MaskCandidates.Find(modelsDir, MaskCandidates.SamDecoder);
        var modnetPath = MaskCandidates.Find(modelsDir, MaskCandidates.Modnet);
        if (encoderPath is null || decoderPath is null || modnetPath is null)
        {
            Console.Error.WriteLine("mask-sheet needs mobile_sam.encoder.onnx, mobile_sam.decoder.onnx and modnet.onnx (exact SHA-256) in --models.");
            return 2;
        }

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.WorkingSet64;
        using var sam = new MobileSamMasker(encoderPath, decoderPath);
        using var modnet = new ModnetMasker(modnetPath);
        process.Refresh();
        var loaded = process.WorkingSet64;

        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var minScore = AiThresholds.Default.FaceMinScore;
        var id = "mask-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        Directory.CreateDirectory(outDir);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var random = new Random(seed);
        var items = new List<MaskItem>();
        int noSubject = 0, unreadable = 0, warmups = 0;

        foreach (var file in JudgingSheets.Sample(Directory.EnumerateFiles(photosDir).Where(IsJpeg), seed))
        {
            if (items.Count >= count)
            {
                break;
            }

            AnalysisImage image, display;
            try
            {
                image = loader.Load(file, MaskLongSide);
                display = loader.Load(file, JudgingSheets.DisplayLongSide);
            }
            catch (PhotoReadException)
            {
                unreadable++;
                continue;
            }

            var faces = detector.Detect(image, 0.5f)
                .Where(f => f.Score >= minScore)
                .Select(f => new FaceFinding(f.X, f.Y, f.Width, f.Height, f.Score, AiAnalysisService.FaceSharpness(image, f)))
                .ToList();
            var main = AiInsights.MainFaces(faces);
            if (main.Count == 0)
            {
                noSubject++;
                continue;
            }

            if (warmups == 0)
            {
                // The first run of each session includes one-off set-up; keep it out of the timings.
                sam.Mask(image, main);
                modnet.Mask(image);
                warmups++;
            }

            var samMask = sam.Mask(image, main);
            var modnetMask = modnet.Mask(image);
            var itemId = "m" + (items.Count + 1).ToString("000", Culture);
            var samOnLeft = random.Next(2) == 0;
            var samView = MaskMath.Overlay(display, MaskMath.Resize(samMask.Alpha, samMask.Width, samMask.Height, display.Width, display.Height));
            var modnetView = MaskMath.Overlay(display, MaskMath.Resize(modnetMask.Alpha, modnetMask.Width, modnetMask.Height, display.Width, display.Height));
            Save(encoder, display, Path.Combine(img, itemId + "-photo.jpg"));
            Save(encoder, samOnLeft ? samView : modnetView, Path.Combine(img, itemId + "-a.jpg"));
            Save(encoder, samOnLeft ? modnetView : samView, Path.Combine(img, itemId + "-b.jpg"));
            items.Add(new MaskItem(
                itemId, Path.GetFileName(file), samOnLeft, main.Count,
                Math.Round(samMask.EncodeMs, 1), Math.Round(samMask.DecodeMs, 1), Math.Round(modnetMask.EncodeMs, 1),
                Math.Round(MaskMath.Coverage(samMask.Alpha), 4), Math.Round(MaskMath.Coverage(modnetMask.Alpha), 4)));
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();
        process.Refresh();
        var sheet = new JudgingSheets.SheetFile("mask", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), minScore, null, null, items);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new
        {
            id,
            items = items.Select(i => new { id = i.Id, photo = "img/" + i.Id + "-photo.jpg", left = "img/" + i.Id + "-a.jpg", right = "img/" + i.Id + "-b.jpg" }),
        });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(SheetPages.Mask, pageData));

        report.AppendLine(Culture, $"Mask sheet `{id}`: {items.Count} photos with a main face (MobileSAM vs MODNet, sides random, seed {seed}).");
        report.AppendLine(Culture, $"Skipped {noSubject} photo(s) with no main face and {unreadable} unreadable.");
        report.AppendLine();
        AppendTimings(report, items);
        report.AppendLine(Culture, $"- Memory: the two models added {(loaded - before) / 1048576.0:N0} MB to the working set when loaded; peak working set of the whole run {process.PeakWorkingSet64 / 1048576.0:N0} MB (includes photo decoding and the sheet images).");
        return 0;
    }

    internal static void AppendTimings(StringBuilder report, IReadOnlyList<MaskItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        static string Stats(IEnumerable<double> values)
        {
            var list = values.Order().ToList();
            var p90 = list[Math.Min(list.Count - 1, (int)Math.Ceiling(list.Count * 0.9) - 1)];
            return string.Create(Culture, $"median {SheetScoring.Median(list):N0} ms, 90th percentile {p90:N0} ms");
        }

        report.AppendLine(Culture, $"- Masks computed at {MaskLongSide} px (long side), ONNX Runtime CPU, {FrameForge.AI.OnnxAiRuntime.IntraOpThreads} threads, one photo at a time:");
        report.AppendLine(Culture, $"  - MobileSAM encoder: {Stats(items.Select(i => i.SamEncodeMs))}");
        report.AppendLine(Culture, $"  - MobileSAM decoder, all main faces of a photo: {Stats(items.Select(i => i.SamDecodeMs))} (median {SheetScoring.Median(items.Select(i => (double)i.MainFaces).ToList()):0} main face(s) per photo)");
        report.AppendLine(Culture, $"  - MobileSAM total: {Stats(items.Select(i => i.SamEncodeMs + i.SamDecodeMs))}");
        report.AppendLine(Culture, $"  - MODNet: {Stats(items.Select(i => i.ModnetMs))}");
        report.AppendLine(Culture, $"  - Share of the photo inside the mask: MobileSAM median {SheetScoring.Median(items.Select(i => i.SamCoverage).ToList()):P1}, MODNet median {SheetScoring.Median(items.Select(i => i.ModnetCoverage).ToList()):P1}");
    }

    // ------------------------------------------------------------------ round 2: people → MobileSAM

    /// <summary>
    /// Round 2 sheet: YOLOX-s finds the people, the main people (at least half the tallest person's height) prompt
    /// MobileSAM with their boxes, and the combined mask is graded once per photo. Photos in <paramref name="excludeFiles"/>
    /// (round 1) are skipped so the grades are on fresh photos.
    /// </summary>
    public static int BuildPeople(string modelsDir, string photosDir, string outDir, int count, int seed, IReadOnlySet<string> excludeFiles, StringBuilder report)
    {
        var encoderPath = MaskCandidates.Find(modelsDir, MaskCandidates.SamEncoder);
        var decoderPath = MaskCandidates.Find(modelsDir, MaskCandidates.SamDecoder);
        var detectorPath = MaskCandidates.Find(modelsDir, MaskCandidates.PersonDetector);
        if (encoderPath is null || decoderPath is null || detectorPath is null)
        {
            Console.Error.WriteLine("mask-sheet round 2 needs mobile_sam.encoder.onnx, mobile_sam.decoder.onnx and object_detection_yolox_2022nov.onnx (exact SHA-256) in --models.");
            return 2;
        }

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.WorkingSet64;
        using var sam = new MobileSamMasker(encoderPath, decoderPath);
        using var detector = new YoloxPersonDetector(detectorPath);
        process.Refresh();
        var loaded = process.WorkingSet64;

        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var id = "people-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        Directory.CreateDirectory(outDir);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var items = new List<PersonMaskItem>();
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
                image = loader.Load(file, MaskLongSide);
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
                var (first, _) = detector.Detect(image);
                sam.Mask(image, MaskMath.MainPeople(first.Select(p => p.Box).ToList()));
                warmedUp = true;
            }

            var (people, detectMs) = detector.Detect(image);
            var main = MaskMath.MainPeople(people.Select(p => p.Box).ToList());
            if (main.Count == 0)
            {
                noPerson++;
                continue;
            }

            var mask = sam.Mask(image, main);
            var itemId = "s" + (items.Count + 1).ToString("000", Culture);
            var view = MaskMath.Overlay(display, MaskMath.Resize(mask.Alpha, mask.Width, mask.Height, display.Width, display.Height));
            Save(encoder, display, Path.Combine(img, itemId + "-photo.jpg"));
            Save(encoder, view, Path.Combine(img, itemId + "-mask.jpg"));
            items.Add(new PersonMaskItem(
                itemId, Path.GetFileName(file), people.Count, main.Count,
                Math.Round(detectMs, 1), Math.Round(mask.EncodeMs, 1), Math.Round(mask.DecodeMs, 1), Math.Round(MaskMath.Coverage(mask.Alpha), 4)));
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();
        process.Refresh();
        var sheet = new JudgingSheets.SheetFile("mask-people", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), 0, null, null, null, items);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new { id, items = items.Select(i => new { id = i.Id, photo = "img/" + i.Id + "-photo.jpg", mask = "img/" + i.Id + "-mask.jpg" }) });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(SheetPages.MaskSingle, pageData));

        report.AppendLine(Culture, $"People mask sheet `{id}`: {items.Count} photos (YOLOX-s people → MobileSAM, seed {seed}).");
        report.AppendLine(Culture, $"Skipped {excluded} photo(s) used in round 1, {noPerson} with no person found and {unreadable} unreadable.");
        report.AppendLine();
        AppendPeopleTimings(report, items);
        report.AppendLine(Culture, $"- Memory: the two models added {(loaded - before) / 1048576.0:N0} MB to the working set when loaded; peak working set of the whole run {process.PeakWorkingSet64 / 1048576.0:N0} MB (includes photo decoding and the sheet images).");
        return 0;
    }

    internal static void AppendPeopleTimings(StringBuilder report, IReadOnlyList<PersonMaskItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        static string Stats(IEnumerable<double> values)
        {
            var list = values.Order().ToList();
            var p90 = list[Math.Min(list.Count - 1, (int)Math.Ceiling(list.Count * 0.9) - 1)];
            return string.Create(Culture, $"median {SheetScoring.Median(list):N0} ms, 90th percentile {p90:N0} ms");
        }

        report.AppendLine(Culture, $"- Computed at {MaskLongSide} px (long side), ONNX Runtime CPU, {FrameForge.AI.OnnxAiRuntime.IntraOpThreads} threads, one photo at a time:");
        report.AppendLine(Culture, $"  - Person detector (YOLOX-s, 640 px): {Stats(items.Select(i => i.DetectMs))}");
        report.AppendLine(Culture, $"  - MobileSAM encoder: {Stats(items.Select(i => i.EncodeMs))}");
        report.AppendLine(Culture, $"  - MobileSAM decoder, all main people of a photo: {Stats(items.Select(i => i.DecodeMs))}");
        report.AppendLine(Culture, $"  - Total per photo: {Stats(items.Select(i => i.DetectMs + i.EncodeMs + i.DecodeMs))}");
        report.AppendLine(Culture, $"  - People found per photo: median {SheetScoring.Median(items.Select(i => (double)i.People).ToList()):0}, of which main: median {SheetScoring.Median(items.Select(i => (double)i.MainPeople).ToList()):0}");
        report.AppendLine(Culture, $"  - Share of the photo inside the mask: median {SheetScoring.Median(items.Select(i => i.Coverage).ToList()):P1}");
    }

    public static int ScorePeople(JudgingSheets.SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var items = sheet.People ?? [];
        var rows = items.Where(i => raw.ContainsKey(i.Id)).Select(i => (Item: i, Grade: ParseGrade(i.Id, raw[i.Id][0]))).ToList();
        int good = rows.Count(r => r.Grade == MaskGrade.Good), usable = rows.Count(r => r.Grade == MaskGrade.Usable), bad = rows.Count(r => r.Grade == MaskGrade.Bad);
        var (low, high) = SheetScoring.Wilson(good + usable, rows.Count);

        report.AppendLine(Culture, $"## Subject masks, round 2: people → MobileSAM — sheet `{sheet.Id}`");
        report.AppendLine();
        report.AppendLine(Culture, $"- Photos answered: {rows.Count} of {items.Count}");
        report.AppendLine(Culture, $"- **MobileSAM, prompted by the people found:** good {good} · usable {usable} · bad {bad}; good or usable **{(rows.Count == 0 ? 0 : (good + usable) / (double)rows.Count):P0}** (95 % interval {low:P0}–{high:P0})");
        foreach (var group in rows.GroupBy(r => r.Item.MainPeople == 1 ? "one main person" : "several main people"))
        {
            var ok = group.Count(r => r.Grade != MaskGrade.Bad);
            report.AppendLine(Culture, $"  - {group.Key}: good or usable on {ok} of {group.Count()}");
        }

        report.AppendLine();
        AppendPeopleTimings(report, items);
        report.AppendLine();
        report.AppendLine("| Photo | File | People found | Main | Grade |");
        report.AppendLine("|---|---|---:|---:|---|");
        foreach (var (item, grade) in rows)
        {
            report.AppendLine(Culture, $"| {item.Id} | {item.File} | {item.People} | {item.MainPeople} | {grade} |");
        }

        report.AppendLine();
        report.AppendLine("Good = would use as is; usable = would use after a small fix; bad = wrong subject, big parts missing or much background included. One person's grades on one set of photos.");
        return 0;
    }

    public static MaskGrade ParseGrade(string id, string value) => value.Trim().ToLowerInvariant() switch
    {
        "good" => MaskGrade.Good,
        "usable" => MaskGrade.Usable,
        "bad" => MaskGrade.Bad,
        _ => throw new FormatException(string.Create(Culture, $"The answer for {id}: \"{value}\" is not good, usable or bad.")),
    };

    /// <summary>Grades per model: (MobileSAM, MODNet) for each answered photo.</summary>
    internal static List<(MaskItem Item, MaskGrade Sam, MaskGrade Modnet)> Pair(IReadOnlyList<MaskItem> items, Dictionary<string, string[]> raw)
    {
        var result = new List<(MaskItem, MaskGrade, MaskGrade)>();
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

            var left = ParseGrade(item.Id, answer[0]);
            var right = ParseGrade(item.Id, answer[1]);
            result.Add(item.SamOnLeft ? (item, left, right) : (item, right, left));
        }

        return result;
    }

    public static int Score(JudgingSheets.SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var items = sheet.Masks ?? [];
        var rows = Pair(items, raw);
        report.AppendLine(Culture, $"## Subject masks: MobileSAM vs MODNet — sheet `{sheet.Id}`");
        report.AppendLine();
        report.AppendLine(Culture, $"- Photos answered: {rows.Count} of {items.Count}");
        foreach (var (name, pick) in new (string, Func<(MaskItem Item, MaskGrade Sam, MaskGrade Modnet), MaskGrade>)[] { ("MobileSAM", r => r.Sam), ("MODNet", r => r.Modnet) })
        {
            var grades = rows.Select(pick).ToList();
            int good = grades.Count(g => g == MaskGrade.Good), usable = grades.Count(g => g == MaskGrade.Usable), bad = grades.Count(g => g == MaskGrade.Bad);
            var (low, high) = SheetScoring.Wilson(good + usable, grades.Count);
            report.AppendLine(Culture, $"- **{name}:** good {good} · usable {usable} · bad {bad}; good or usable {(grades.Count == 0 ? 0 : (good + usable) / (double)grades.Count):P0} (95 % interval {low:P0}–{high:P0})");
        }

        int samBetter = rows.Count(r => r.Sam < r.Modnet), modnetBetter = rows.Count(r => r.Modnet < r.Sam);
        report.AppendLine(Culture, $"- Graded better: MobileSAM on {samBetter}, MODNet on {modnetBetter}, equal on {rows.Count - samBetter - modnetBetter}; sign test p = {SheetScoring.SignTestP(samBetter, modnetBetter):0.000}");
        report.AppendLine();
        AppendTimings(report, items);
        report.AppendLine();
        report.AppendLine("| Photo | File | Main faces | MobileSAM | MODNet |");
        report.AppendLine("|---|---|---:|---|---|");
        foreach (var (item, s, m) in rows)
        {
            report.AppendLine(Culture, $"| {item.Id} | {item.File} | {item.MainFaces} | {s} | {m} |");
        }

        report.AppendLine();
        report.AppendLine("Good = would use as is; usable = would use after a small fix; bad = wrong subject, big parts missing or much background included. One person's grades on one set of photos.");
        return 0;
    }

    private static bool IsJpeg(string f) => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static void Save(WicJpegEncoder encoder, AnalysisImage image, string path)
    {
        using var stream = File.Create(path);
        encoder.Write(image, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 90);
    }
}
