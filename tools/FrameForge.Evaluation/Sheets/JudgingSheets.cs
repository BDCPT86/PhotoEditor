using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Editing.Auto;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Imaging;
using FrameForge.Rendering;

namespace FrameForge.Evaluation.Sheets;

/// <summary>
/// Builds and scores the human judging sheets for Milestone 08's Auto tools:
/// <list type="bullet">
/// <item><b>light</b>: blind A/B of each photo as shot against Auto light's Natural option, sides randomised.</item>
/// <item><b>crop</b>: the user crops photos with faces; their framing is compared with Auto crop at the same ratio.</item>
/// <item><b>mask</b>: subject masks from two candidate models, graded blind (see <see cref="MaskSheet"/>).</item>
/// </list>
/// A sheet is a folder: <c>index.html</c> (the page), <c>img/</c> (downscaled copies) and <c>sheet.json</c> (the key used
/// for scoring; not loaded by the page, so the A/B stays blind). Originals are only read.
/// </summary>
internal static class JudgingSheets
{
    public const int DisplayLongSide = 1600;
    private const int MeasureLongSide = 2048; // Auto light measures at the Develop preview size
    private const int FaceLongSide = 1024; // faces are detected at the default analysis size, as in the app
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal sealed record SheetFile(string Kind, string Id, DateTimeOffset Created, string PhotosFolder, double FaceMinScore, List<LightPair>? Pairs, List<CropItem>? Items, List<MaskItem>? Masks = null, List<PersonMaskItem>? People = null, List<PaddingItem>? Padding = null, List<SkyItem>? Sky = null, List<DrawItem>? Draw = null);

    /// <summary>Photo files in a stable, seeded random order, so a sheet can be rebuilt exactly and sampling is not biased to the start of a shoot.</summary>
    internal static List<string> Sample(IEnumerable<string> files, int seed)
    {
        // Sort first so the order does not depend on the file system, then a seeded Fisher–Yates shuffle.
        var list = files.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var random = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    internal static string? CheckFolders(string photosDir, string outDir)
    {
        if (!Directory.Exists(photosDir))
        {
            return "The photos folder does not exist: " + photosDir;
        }

        var photos = Path.GetFullPath(photosDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var output = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.StartsWith(photos, StringComparison.OrdinalIgnoreCase))
        {
            return "--sheet must not be inside the photos folder (originals stay untouched).";
        }

        return Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any()
            ? "--sheet must be a new or empty folder, so an existing sheet and its answers are never overwritten."
            : null;
    }

    private static IEnumerable<string> Jpegs(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ light

    public static string BuildLight(string photosDir, string outDir, int count, int seed, StringBuilder report)
    {
        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var id = "light-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var random = new Random(seed);
        var pairs = new List<LightPair>();
        int leftAsShot = 0, unreadable = 0;

        foreach (var file in Sample(Jpegs(photosDir), seed))
        {
            if (pairs.Count >= count)
            {
                break;
            }

            AnalysisImage source;
            try
            {
                source = loader.Load(file, MeasureLongSide);
            }
            catch (PhotoReadException)
            {
                unreadable++;
                continue;
            }

            var natural = AutoTone.Suggest(source, EditRecipe.Neutral).Named("Natural");
            if (natural is null || natural.Recipe.IsNeutral)
            {
                leftAsShot++; // nothing to compare: Auto would change nothing
                continue;
            }

            var pairId = "p" + (pairs.Count + 1).ToString("000", Culture);
            var autoOnLeft = random.Next(2) == 0;
            var original = EditRenderer.Render(source, EditRecipe.Neutral, DisplayLongSide);
            var auto = EditRenderer.Render(source, natural.Recipe, DisplayLongSide);
            Save(encoder, autoOnLeft ? auto : original, Path.Combine(img, pairId + "-a.jpg"));
            Save(encoder, autoOnLeft ? original : auto, Path.Combine(img, pairId + "-b.jpg"));
            pairs.Add(new LightPair(pairId, Path.GetFileName(file), autoOnLeft, natural.Name, Describe(natural.Recipe)));
        }

        var sheet = new SheetFile("light", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), AiThresholds.Default.FaceMinScore, pairs, null);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new { id, items = pairs.Select(p => new { id = p.Id, left = "img/" + p.Id + "-a.jpg", right = "img/" + p.Id + "-b.jpg" }) });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(SheetPages.Light, pageData));

        report.AppendLine(Culture, $"Light sheet `{id}`: {pairs.Count} pairs (Auto light \"Natural\" vs as shot, sides random, seed {seed}).");
        report.AppendLine(Culture, $"Skipped {leftAsShot} photo(s) Auto would leave unchanged and {unreadable} unreadable.");
        return id;
    }

    /// <summary>A short summary of what a recipe changes, for the report (never shown on the page).</summary>
    internal static string Describe(EditRecipe r)
    {
        var parts = new List<string>();
        void Add(string name, double value, string format)
        {
            if (Math.Abs(value) > 1e-9)
            {
                parts.Add(name + " " + value.ToString(format, Culture));
            }
        }

        Add("exposure", r.Exposure, "+0.00;-0.00");
        Add("contrast", r.Contrast, "+0;-0");
        Add("highlights", r.Highlights, "+0;-0");
        Add("shadows", r.Shadows, "+0;-0");
        Add("temperature", r.Temperature, "+0;-0");
        Add("tint", r.Tint, "+0;-0");
        Add("vibrance", r.Vibrance, "+0;-0");
        Add("saturation", r.Saturation, "+0;-0");
        return parts.Count == 0 ? "no change" : string.Join(", ", parts);
    }

    // ------------------------------------------------------------------ crop

    public static string BuildCrop(string photosDir, string outDir, int count, int seed, IFaceDetector detector, StringBuilder report)
    {
        var loader = new WicAnalysisImageLoader();
        var metadata = new WicPhotoMetadataReader();
        var encoder = new WicJpegEncoder();
        var minScore = AiThresholds.Default.FaceMinScore;
        var id = "crop-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var items = new List<CropItem>();
        var pageItems = new List<object>();
        int noSubject = 0, unreadable = 0;

        foreach (var file in Sample(Jpegs(photosDir), seed))
        {
            if (items.Count >= count)
            {
                break;
            }

            AnalysisImage forFaces, display;
            (int W, int H) size;
            try
            {
                forFaces = loader.Load(file, FaceLongSide);
                display = loader.Load(file, DisplayLongSide);
                var meta = metadata.Read(file);
                size = meta.Width is { } w && meta.Height is { } h
                    ? meta.Orientation is >= 5 and <= 8 ? (h, w) : (w, h)
                    : (display.Width, display.Height);
            }
            catch (PhotoReadException)
            {
                unreadable++;
                continue;
            }

            var faces = detector.Detect(forFaces, 0.5f)
                .Select(f => new FaceFinding(f.X, f.Y, f.Width, f.Height, f.Score, AiAnalysisService.FaceSharpness(forFaces, f)))
                .ToList();
            var item = new CropItem("c" + (items.Count + 1).ToString("000", Culture), Path.GetFileName(file), size.W, size.H, faces);

            // Only photos Auto crop can work on (a main face), or there is nothing to compare. "Already framed" photos stay
            // in: Auto declining to crop is a decision too, and leaving them out would hide the keep-the-frame cases.
            var suggestion = AutoCrop.Suggest(size.W, size.H, faces, EditRecipe.Neutral, null, minScore);
            if (!suggestion.HasOptions && suggestion.Unavailable != AutoCrop.AlreadyFramed)
            {
                noSubject++;
                continue;
            }

            Save(encoder, display, Path.Combine(img, item.Id + ".jpg"));
            items.Add(item);
            pageItems.Add(new { id = item.Id, img = "img/" + item.Id + ".jpg", w = size.W, h = size.H });
        }

        var sheet = new SheetFile("crop", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), minScore, null, items);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(SheetPages.Crop, JsonSerializer.Serialize(new { id, items = pageItems })));

        report.AppendLine(Culture, $"Crop sheet `{id}`: {items.Count} photos with a main face (seed {seed}).");
        report.AppendLine(Culture, $"Skipped {noSubject} photo(s) with no main face and {unreadable} unreadable.");
        return id;
    }

    private static void Save(WicJpegEncoder encoder, AnalysisImage image, string path)
    {
        using var stream = File.Create(path);
        encoder.Write(image, null, stream, 90);
    }

    // ------------------------------------------------------------------ scoring

    internal static SheetFile ReadSheet(string sheetDir) =>
        JsonSerializer.Deserialize<SheetFile>(File.ReadAllText(Path.Combine(sheetDir, "sheet.json")), Json)
            ?? throw new InvalidDataException("sheet.json is empty.");

    public static int Score(string sheetDir, string answersPath, StringBuilder report)
    {
        var sheet = JsonSerializer.Deserialize<SheetFile>(File.ReadAllText(Path.Combine(sheetDir, "sheet.json")), Json)
            ?? throw new InvalidDataException("sheet.json is empty.");
        if (sheet.Kind == "mask-draw")
        {
            return DrawSheet.Score(sheetDir, sheet, answersPath, report);
        }

        var raw = SheetScoring.ParseAnswers(File.ReadLines(answersPath));
        return sheet.Kind switch
        {
            "light" => ScoreLight(sheet, raw, report),
            "crop" => ScoreCrop(sheet, raw, report),
            "mask" => MaskSheet.Score(sheet, raw, report),
            "mask-people" => MaskSheet.ScorePeople(sheet, raw, report),
            "mask-padding" => PaddingSheet.Score(sheet, raw, report),
            "mask-sky" => SkySheet.Score(sheet, raw, report),
            _ => throw new InvalidDataException("Unknown sheet kind: " + sheet.Kind),
        };
    }

    private static int ScoreLight(SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var pairs = sheet.Pairs ?? [];
        var answers = raw.Where(kv => pairs.Any(p => p.Id == kv.Key)).ToDictionary(kv => kv.Key, kv => SheetScoring.ParseLightChoice(kv.Key, kv.Value[0]));
        var tally = SheetScoring.TallyLight(pairs, answers);
        var (low, high) = SheetScoring.Wilson(tally.Auto, tally.Decided);

        report.AppendLine(Culture, $"## Auto light blind A/B — sheet `{sheet.Id}`");
        report.AppendLine();
        report.AppendLine(Culture, $"- Pairs answered: {pairs.Count - tally.Unanswered} of {pairs.Count}");
        report.AppendLine(Culture, $"- Preferred Auto light: **{tally.Auto}** · preferred as shot: **{tally.Original}** · no real difference: {tally.Same}");
        if (tally.AutoShare is { } share)
        {
            report.AppendLine(Culture, $"- Auto preferred in {share:P0} of the pairs where you chose (95 % interval {low:P0}–{high:P0}); sign test p = {SheetScoring.SignTestP(tally.Auto, tally.Original):0.000}");
        }

        report.AppendLine();
        report.AppendLine("| Pair | Photo | What Auto changed | Your pick |");
        report.AppendLine("|---|---|---|---|");
        foreach (var p in pairs)
        {
            var pick = !answers.TryGetValue(p.Id, out var c) ? "–" : c == LightChoice.Same ? "no real difference" : (c == LightChoice.Left) == p.AutoOnLeft ? "**Auto**" : "as shot";
            report.AppendLine(Culture, $"| {p.Id} | {p.File} | {p.Change} | {pick} |");
        }

        report.AppendLine();
        report.AppendLine("One person's preference on one set of photos: it shows whether Auto light helps *you* on *these* photos, not general quality. With few decided pairs the interval is wide; read it before the percentage.");
        return 0;
    }

    private static UserCrop? ParseCropFor(string id, string[] answer)
    {
        try
        {
            return SheetScoring.ParseCrop(answer);
        }
        catch (FormatException ex)
        {
            throw new FormatException("The answer for " + id + ": " + ex.Message, ex);
        }
    }

    private static int ScoreCrop(SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var items = sheet.Items ?? [];
        var rows = new List<SheetScoring.CropComparison>();
        foreach (var item in items)
        {
            if (raw.TryGetValue(item.Id, out var answer))
            {
                rows.Add(SheetScoring.CompareCrop(item, ParseCropFor(item.Id, answer), sheet.FaceMinScore));
            }
        }

        var cropped = rows.Where(r => !r.UserKeptFrame && r.BestIou is not null).ToList();
        var ious = cropped.Select(r => r.BestIou!.Value).ToList();
        report.AppendLine(Culture, $"## Auto crop agreement — sheet `{sheet.Id}`");
        report.AppendLine();
        var kept = rows.Where(r => r.UserKeptFrame).ToList();
        // Auto agrees when it declines to crop (already framed loosely) or its widest option is nearly the whole frame.
        var keptAgreed = kept.Count(r => r.BestIou >= 0.95 || r.Unavailable == AutoCrop.AlreadyFramed);
        report.AppendLine(Culture, $"- Photos answered: {rows.Count} of {items.Count}; you cropped {rows.Count - kept.Count} and kept the whole frame on {kept.Count}.");
        if (kept.Count > 0)
        {
            report.AppendLine(Culture, $"- Where you kept the whole frame, Auto also left it (nearly) uncropped on {keptAgreed} of {kept.Count}; on the rest Auto would have cropped.");
        }

        var declined = rows.Count(r => !r.UserKeptFrame && r.Unavailable == AutoCrop.AlreadyFramed);
        if (declined > 0)
        {
            report.AppendLine(Culture, $"- Where you cropped, Auto said the photo was already framed loosely (no crop) on {declined}; these are not in the overlap figures.");
        }
        if (ious.Count > 0)
        {
            report.AppendLine(Culture, $"- Overlap with Auto's closest option (IoU, 1 = identical): median **{SheetScoring.Median(ious):0.00}**, mean {ious.Average():0.00}");
            report.AppendLine(Culture, $"- Close match (IoU ≥ 0.75): {ious.Count(v => v >= 0.75)} of {ious.Count} · similar (≥ 0.5): {ious.Count(v => v >= 0.5)} · different (< 0.5): {ious.Count(v => v < 0.5)}");
            report.AppendLine(Culture, $"- Closest option: {string.Join(", ", cropped.GroupBy(r => r.BestOption).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
            var areas = cropped.Where(r => r.AreaRatio is not null).Select(r => r.AreaRatio!.Value).ToList();
            var shifts = cropped.Where(r => r.CentreShift is not null).Select(r => r.CentreShift!.Value).ToList();
            if (areas.Count > 0)
            {
                report.AppendLine(Culture, $"- Your crop vs Auto's closest: median area ratio {SheetScoring.Median(areas):0.00} (above 1 = you framed looser; looser on {areas.Count(a => a > 1.1)}, tighter on {areas.Count(a => a < 0.9)} of {areas.Count})");
                report.AppendLine(Culture, $"- Centre shift (yours minus Auto's, share of the frame): median across {SheetScoring.Median(shifts.Select(s => s.X).ToList()):+0.00;-0.00}, down {SheetScoring.Median(shifts.Select(s => s.Y).ToList()):+0.00;-0.00}; median distance {SheetScoring.Median(shifts.Select(s => Math.Sqrt((s.X * s.X) + (s.Y * s.Y))).ToList()):0.00}");
            }

            var mediums = cropped.Where(r => r.MediumIou is not null).Select(r => r.MediumIou!.Value).ToList();
            if (mediums.Count > 0)
            {
                report.AppendLine(Culture, $"- Medium alone (what batch Auto uses by default): median IoU {SheetScoring.Median(mediums):0.00}");
            }
        }

        var tight = rows.Count(r => r.UserBelowMinimum);
        if (tight > 0)
        {
            report.AppendLine(Culture, $"- {tight} of your crops are tighter than Auto's {AutoCrop.DefaultMinLongSide:N0} px minimum long side, so Auto could not match them by design.");
        }

        var unavailable = rows.Count(r => r.Unavailable is not null && r.Unavailable != AutoCrop.AlreadyFramed);
        if (unavailable > 0)
        {
            report.AppendLine(Culture, $"- Auto could not frame the faces at your ratio on {unavailable} photo(s).");
        }

        report.AppendLine();
        report.AppendLine("| Photo | File | Your crop | Closest Auto option | IoU |");
        report.AppendLine("|---|---|---|---|---:|");
        foreach (var r in rows)
        {
            var yours = r.UserKeptFrame ? "whole frame" : r.UserBelowMinimum ? "cropped (below min size)" : "cropped";
            report.AppendLine(Culture, $"| {r.Id} | {r.File} | {yours} | {r.BestOption ?? r.Unavailable ?? "–"} | {(r.BestIou is { } v ? v.ToString("0.00", Culture) : "–")} |");
        }

        report.AppendLine();
        report.AppendLine("Auto's options are computed at the aspect ratio of your crop, so this measures framing, not the choice of ratio. Agreement with one person's taste on one set of photos, not a quality score.");
        return 0;
    }
}
