using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FrameForge.AI;
using FrameForge.Analysis;
using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;
using FrameForge.Evaluation;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging.Abstractions;

// FrameForge model benchmark and evaluation (developer tool).
//   benchmark --models <dir> [--count 100] [--out report.md]
//   evaluate  --models <dir> --photos <dir> --labels <labels.csv> [--out report.md]
//   annotate  --models <dir> --photos <dir> --annotations <dir>   (faces.csv + marked-up copies for visual checks)
//   review    --photos <dir> --sheets <dir> [--count 6]           (edit contact sheets + render timings; no models needed)
//   light-sheet --photos <dir> --sheet <new dir> [--count 30] [--seed 1]                 (blind A/B page: Auto light vs as shot)
//   crop-sheet  --models <dir> --photos <dir> --sheet <new dir> [--count 30] [--seed 1]  (page where you crop photos with faces)
//   mask-sheet  --models <dir> --photos <dir> --sheet <new dir> [--count 30] [--seed 1] [--round 1|2|3] [--exclude <sheet dir>[;<sheet dir>…]]  (subject masks; round 1 = MobileSAM vs MODNet from faces, round 2 = people → MobileSAM, round 3 = padded vs unpadded boxes)
//   score-sheet --sheet <dir> --answers <saved csv> [--out report.md]                  (compares your answers with Auto)
//   mask-benchmark --models <dir> --photos <dir> [--count 20] [--seed 1] [--threads 2,4,6]  (time per mask stage and memory on real photos; thread counts interleaved per photo)
//   sky-sheet --photos <dir> --sheet <new dir> [--count 30] [--seed 1] [--exclude <sheet dir>[;<sheet dir>…]]  (grades the sky rules; about two thirds with sky found)
//   draw-sheet --models <dir> --photos <dir> --sheet <new dir> [--count 12] [--seed 1] [--exclude <sheet dir>[;…]]  (you outline subject and sky; scored as IoU against the app's masks)
//   raw-spike --raw <dir> [--jpeg <dir>] --libraw <libraw.dll> --out-dir <new dir> [--count 20] [--out report.md]  (M07 A1: LibRaw on your RAW files: speed, memory, colour against the camera JPEG, damaged copies)
//   perf --work <new dir> [--sizes 1000,10000,50000] [--photos <dir>] [--count 30]  (M07 D1: import and startup loads at library scale, per-photo costs and export memory on real photos)
//   brand --xaml <src/FrameForge.App/Themes/Brand.xaml> --dir <output dir>  (renders the app icon, splash image and a 256 px mark from the logo)
// <dir> holds the catalogued model files (verified by SHA-256 before use). Photos are opened read-only.
// Before anything touches ONNX Runtime: its telemetry is on by default (ADR 0018).
OnnxPrivacy.EnsureApplied();

var culture = CultureInfo.InvariantCulture;
if (args.Length == 0 || args[0] is not ("benchmark" or "evaluate" or "annotate" or "review" or "light-sheet" or "crop-sheet" or "mask-sheet" or "score-sheet" or "mask-benchmark" or "sky-sheet" or "draw-sheet" or "sky-debug" or "raw-spike" or "raw-decode-one" or "perf" or "brand"))
{
    Console.Error.WriteLine("Usage:\n  benchmark --models <dir> [--count 100] [--out report.md]\n  evaluate --models <dir> --photos <dir> --labels <labels.csv> [--out report.md]\n  annotate --models <dir> --photos <dir> --annotations <dir>\n  review --photos <dir> --sheets <dir> [--count 6]\n  light-sheet --photos <dir> --sheet <new dir> [--count 30] [--seed 1]\n  crop-sheet --models <dir> --photos <dir> --sheet <new dir> [--count 30] [--seed 1]\n  mask-sheet --models <dir> --photos <dir> --sheet <new dir> [--count 30] [--seed 1] [--round 1|2|3] [--exclude <dir>[;<dir>]]\n  score-sheet --sheet <dir> --answers <saved csv> [--out report.md]");
    return 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
if (args[0] == "raw-decode-one")
{
    // Child process for raw-spike's damaged-file test: a native crash here ends only this process.
    return FrameForge.Evaluation.Raw.RawSpike.DecodeOne(options["libraw"], options["file"]);
}

var needsModels = args[0] is not ("review" or "light-sheet" or "score-sheet" or "mask-benchmark" or "sky-sheet" or "draw-sheet" or "sky-debug" or "raw-spike" or "perf" or "brand");
if (!options.TryGetValue("models", out var modelsDir) && needsModels)
{
    Console.Error.WriteLine("--models <dir> is required.");
    return 2;
}

var store = new FolderModelStore(modelsDir ?? string.Empty);
using var runtime = new OnnxAiRuntime(store, NullLogger<OnnxAiRuntime>.Instance);
var detector = needsModels ? runtime.GetFaceDetector() : null;
var embedder = needsModels ? runtime.GetImageEmbedder() : null;
if (needsModels && detector is null && embedder is null)
{
    Console.Error.WriteLine("No verified model found in " + modelsDir + (runtime.LastError is { } e ? ": " + e : "."));
    return 1;
}

var report = new StringBuilder();
report.AppendLine(culture, $"# FrameForge AI {args[0]} — {DateTime.Now:yyyy-MM-dd HH:mm}");
report.AppendLine();
report.AppendLine(culture, $"- Machine: {Environment.ProcessorCount} logical CPUs · {Environment.OSVersion} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
report.AppendLine(culture, $"- Runtime: ONNX Runtime CPU, {(args[0] == "mask-benchmark" ? "thread counts as below" : OnnxAiRuntime.IntraOpThreads + " intra-op threads per session")}, one photo at a time");
report.AppendLine(culture, $"- Face detector: {detector?.VersionTag ?? "not installed"}");
report.AppendLine(culture, $"- Scene embedder: {embedder?.VersionTag ?? "not installed"}");
report.AppendLine();

var exit = args[0] switch
{
    "benchmark" => Benchmark(int.Parse(options.GetValueOrDefault("count", "100"), culture)),
    "annotate" => Annotate(options),
    "review" => Review(options),
    "light-sheet" or "crop-sheet" or "mask-sheet" or "sky-sheet" or "draw-sheet" => BuildSheet(args[0], options),
    "score-sheet" => ScoreSheet(options),
    "mask-benchmark" => MaskBenchmarkCommand(options),
    "sky-debug" => SkyDebugCommand(options),
    "raw-spike" => RawSpikeCommand(options),
    "perf" => PerfCommand(options),
    "brand" => BrandCommand(options),
    _ => Evaluate(options),
};

var text = report.ToString();
Console.WriteLine(text);
if (exit == 0 && options.TryGetValue("out", out var outPath))
{
    File.WriteAllText(outPath, text);
    Console.WriteLine("Report written to " + outPath);
}

return exit;

int Benchmark(int count)
{
    // Synthetic 1024×683 images (the default analysis resolution) — no user photos involved.
    var images = Enumerable.Range(0, Math.Min(count, 8))
        .Select(seed => AnalysisImage.Create(1024, 683, (x, y) => ((byte)((x * (3 + seed)) ^ y), (byte)((y * (5 + seed)) + (x / 4)), (byte)((x + y) * (1 + seed)))))
        .ToList();

    // Warm-up (first run includes graph optimisation) is reported separately.
    var warm = Stopwatch.StartNew();
    detector?.Detect(images[0], 0.5f);
    embedder?.Embed(images[0]);
    var warmup = warm.Elapsed;

    var faceMs = new List<double>();
    var embedMs = new List<double>();
    for (var i = 0; i < count; i++)
    {
        var image = images[i % images.Count];
        if (detector is not null)
        {
            var sw = Stopwatch.StartNew();
            detector.Detect(image, 0.5f);
            faceMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        if (embedder is not null)
        {
            var sw = Stopwatch.StartNew();
            embedder.Embed(image);
            embedMs.Add(sw.Elapsed.TotalMilliseconds);
        }
    }

    report.AppendLine(culture, $"Synthetic 1024×683 images, {count} runs per model (inference + preprocessing; excludes JPEG decoding). First call (load + warm-up): {warmup.TotalMilliseconds:0} ms.");
    report.AppendLine();
    report.AppendLine("| Model | median ms | p95 ms | est. time per 1,000 photos (one photo at a time) |");
    report.AppendLine("|---|---:|---:|---:|");
    Row("YuNet face detector", faceMs);
    Row("DINOv2-small embedder", embedMs);
    report.AppendLine();
    report.AppendLine(culture, $"Peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d:0} MB (whole process, both models loaded).");
    report.AppendLine();
    report.AppendLine("These are speed figures only. They say nothing about detection or grouping quality.");
    return 0;

    void Row(string name, List<double> ms)
    {
        if (ms.Count == 0)
        {
            report.AppendLine(culture, $"| {name} | not installed | | |");
            return;
        }

        ms.Sort();
        var median = ms[ms.Count / 2];
        var p95 = ms[Math.Min(ms.Count - 1, (int)Math.Ceiling(ms.Count * 0.95) - 1)];
        report.AppendLine(culture, $"| {name} | {median:0.0} | {p95:0.0} | {median:0} s |");
    }
}

int Evaluate(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("photos", out var photosDir) || !opts.TryGetValue("labels", out var labelsPath))
    {
        Console.Error.WriteLine("evaluate needs --photos <dir> and --labels <labels.csv>.");
        return 2;
    }

    var labels = EvaluationMetrics.ParseLabels(File.ReadLines(labelsPath));
    var loader = new WicAnalysisImageLoader();
    var metadata = new WicPhotoMetadataReader();
    var settings = AnalysisSettings.Default;
    var photos = new List<Photo>();
    var ai = new Dictionary<Guid, AiPhotoAnalysis>();
    var deterministic = new Dictionary<Guid, PhotoAnalysis>();
    var missing = new List<string>();
    var total = Stopwatch.StartNew();

    foreach (var label in labels)
    {
        var path = Path.Combine(photosDir, label.File);
        if (!File.Exists(path))
        {
            missing.Add(label.File);
            continue;
        }

        var photo = new Photo
        {
            Id = Guid.NewGuid(),
            FilePath = path,
            FileName = label.File,
            FolderPath = photosDir,
            Status = ImportStatus.Imported,
            CaptureLocalTime = metadata.Read(path).CaptureLocalTime,
        };
        var image = loader.Load(path, settings.MaxPixelSize);
        deterministic[photo.Id] = new PhotoAnalysis { Sharpness = SharpnessAnalyzer.Analyze(image, settings.SharpnessGrid) };
        ai[photo.Id] = new AiPhotoAnalysis
        {
            Faces = detector is null ? null : new FacesResult(detector.Detect(image, 0.5f)
                .Select(f => new FaceFinding(f.X, f.Y, f.Width, f.Height, f.Score, AiAnalysisService.FaceSharpness(image, f))).ToList()),
            Embedding = embedder?.Embed(image),
        };
        photos.Add(photo);
    }

    var elapsed = total.Elapsed;
    var thresholds = AiThresholds.Default;
    var insights = AiInsights.Build(photos, ai, deterministic, new Dictionary<Guid, PhotoInsight>(), thresholds);
    var byFile = photos.ToDictionary(p => p.FileName, StringComparer.OrdinalIgnoreCase);
    var labelOf = labels.Where(l => byFile.ContainsKey(l.File)).ToDictionary(l => l.File, StringComparer.OrdinalIgnoreCase);

    report.AppendLine(culture, $"{photos.Count} labelled photos evaluated from the photos folder ({missing.Count} labelled files not found). Default thresholds: face score ≥ {thresholds.FaceMinScore:0.00}, scene similarity ≥ {thresholds.SceneSimilarityMin:0.00}, burst window {thresholds.BurstWindowSeconds} s.");
    report.AppendLine();

    if (embedder is not null)
    {
        var predicted = photos.ToDictionary(p => p.FileName, p => insights.GetValueOrDefault(p.Id)?.SceneGroupKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var labelled = labelOf.ToDictionary(kv => kv.Key, kv => kv.Value.Group, StringComparer.OrdinalIgnoreCase);
        var grouping = EvaluationMetrics.Pairwise(predicted, labelled);
        report.AppendLine(culture, $"- Scene grouping (pairwise): precision {EvaluationMetrics.Percent(grouping.Precision)}, recall {EvaluationMetrics.Percent(grouping.Recall)} ({grouping.TruePositives} of {grouping.Actual} labelled pairs found; {grouping.Predicted} pairs predicted)");
    }

    if (detector is not null)
    {
        var faces = EvaluationMetrics.FaceCounts(photos
            .Where(p => labelOf[p.FileName].Faces is not null)
            .Select(p => (ai[p.Id].Faces!.Faces.Count(f => f.Score >= thresholds.FaceMinScore), labelOf[p.FileName].Faces!.Value)));
        report.AppendLine(culture, $"- Face recall (by count): {EvaluationMetrics.Percent(faces.Recall)}; {faces.Extra} detections beyond the labelled count (possible false positives)");
    }

    var picks = photos.Where(p => insights.GetValueOrDefault(p.Id)?.IsSuggestedPick == true).Select(p => p.FileName).ToList();
    var grouped = photos.Where(p => insights.GetValueOrDefault(p.Id)?.SceneGroupKey is not null).Select(p => p.FileName).ToList();
    var (agreement, falseRejection) = EvaluationMetrics.Picks(picks, grouped, labelOf.ToDictionary(kv => kv.Key, kv => kv.Value.Decision, StringComparer.OrdinalIgnoreCase));
    report.AppendLine(culture, $"- Pick agreement (AI pick labelled keep): {EvaluationMetrics.Percent(agreement)} of {picks.Count} picks");
    report.AppendLine(culture, $"- False rejection (labelled keepers in AI groups not suggested): {EvaluationMetrics.Percent(falseRejection)}");
    report.AppendLine(culture, $"- Latency: {elapsed.TotalSeconds:0.0} s total, {(photos.Count == 0 ? 0 : elapsed.TotalSeconds / photos.Count * 1000 / 60):0.0} min per 1,000 photos (decode + deterministic sharpness + AI, one at a time)");
    report.AppendLine(culture, $"- Peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d:0} MB");
    report.AppendLine();
    report.AppendLine("Metrics are only as good as the labels and the size of the set; report them with the set's size and content.");
    return 0;
}

int Review(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("photos", out var photosDir) || !opts.TryGetValue("sheets", out var outDir))
    {
        Console.Error.WriteLine("review needs --photos <dir> and --sheets <dir>.");
        return 2;
    }

    if (Path.GetFullPath(outDir).StartsWith(Path.GetFullPath(photosDir), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("--sheets must not be inside the photos folder (originals stay untouched).");
        return 2;
    }

    Directory.CreateDirectory(outDir);
    var count = int.Parse(opts.GetValueOrDefault("count", "6"), culture);
    var files = Directory.EnumerateFiles(photosDir, "*.jp*g").Order().ToList();
    var step = Math.Max(1, files.Count / count);
    var chosen = files.Where((_, i) => i % step == 0).Take(count).ToList();
    var loader = new WicAnalysisImageLoader();
    var encoder = new WicJpegEncoder();
    var variants = new (string Name, FrameForge.Core.Editing.EditRecipe Recipe)[]
    {
        ("original", FrameForge.Core.Editing.EditRecipe.Neutral),
        ("+1 EV", FrameForge.Core.Editing.EditRecipe.Neutral with { Exposure = 1 }),
        ("-1 EV", FrameForge.Core.Editing.EditRecipe.Neutral with { Exposure = -1 }),
        ("warm +50", FrameForge.Core.Editing.EditRecipe.Neutral with { Temperature = 50 }),
        ("cool -50", FrameForge.Core.Editing.EditRecipe.Neutral with { Temperature = -50 }),
        ("contrast +50", FrameForge.Core.Editing.EditRecipe.Neutral with { Contrast = 50 }),
        ("highlights -100, shadows +100", FrameForge.Core.Editing.EditRecipe.Neutral with { Highlights = -100, Shadows = 100 }),
        ("saturation -100", FrameForge.Core.Editing.EditRecipe.Neutral with { Saturation = -100 }),
        ("vibrance +80", FrameForge.Core.Editing.EditRecipe.Neutral with { Vibrance = 80 }),
        ("straighten 5°, crop 4:5", FrameForge.Core.Editing.EditRecipe.Neutral with { Straighten = 5, Crop = new FrameForge.Core.Editing.CropRect(0.2, 0, 0.6, 1) }),
    };

    var previewMs = new List<double>();
    var fullMs = new List<double>();
    report.AppendLine(culture, $"Contact sheets for {chosen.Count} photo(s): each tile is one recipe rendered at preview size (columns: {string.Join(" · ", variants.Select(v => v.Name))}).");
    report.AppendLine();
    foreach (var file in chosen)
    {
        var source = loader.Load(file, 2048);
        const int tile = 480;
        var rendered = variants.Select(v =>
        {
            var sw = Stopwatch.StartNew();
            var image = FrameForge.Rendering.EditRenderer.Render(source, v.Recipe);
            previewMs.Add(sw.Elapsed.TotalMilliseconds);
            return FrameForge.Rendering.EditRenderer.Render(image, FrameForge.Core.Editing.EditRecipe.Neutral, tile);
        }).ToList();

        // Full-resolution render of one combined recipe, as export does.
        var full = loader.Load(file, int.MaxValue);
        var fullTimer = Stopwatch.StartNew();
        FrameForge.Rendering.EditRenderer.Render(full, FrameForge.Core.Editing.EditRecipe.Neutral with { Exposure = 0.3, Contrast = 20, Shadows = 30, Saturation = 10, Straighten = 2 });
        fullMs.Add(fullTimer.Elapsed.TotalMilliseconds);

        var sheet = ContactSheet(rendered, tile, columns: 5);
        using var stream = File.Create(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + "_sheet.jpg"));
        encoder.Write(sheet, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 88);
        report.AppendLine(culture, $"- {Path.GetFileName(file)}: {full.Width}×{full.Height}");
    }

    previewMs.Sort();
    fullMs.Sort();
    report.AppendLine();
    report.AppendLine(culture, $"Preview render at 2048 px: median {previewMs[previewMs.Count / 2]:0} ms (max {previewMs[^1]:0} ms) over {previewMs.Count} renders.");
    report.AppendLine(culture, $"Full-resolution render (tone + colour + straighten, decode excluded): median {fullMs[fullMs.Count / 2]:0} ms over {fullMs.Count} photos.");
    report.AppendLine(culture, $"Peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d:0} MB.");
    return 0;

    static FrameForge.Analysis.AnalysisImage ContactSheet(List<FrameForge.Analysis.AnalysisImage> tiles, int size, int columns)
    {
        var rows = (tiles.Count + columns - 1) / columns;
        int width = columns * size, height = rows * size;
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)32);
        for (var t = 0; t < tiles.Count; t++)
        {
            var img = tiles[t];
            int ox = ((t % columns) * size) + ((size - img.Width) / 2), oy = ((t / columns) * size) + ((size - img.Height) / 2);
            for (var y = 0; y < img.Height; y++)
            {
                Buffer.BlockCopy(img.Bgra, y * img.Width * 4, pixels, (((oy + y) * width) + ox) * 4, img.Width * 4);
            }
        }

        return new FrameForge.Analysis.AnalysisImage(width, height, pixels);
    }
}

int Annotate(Dictionary<string, string> opts)
{
    if (detector is null || !opts.TryGetValue("photos", out var photosDir) || !opts.TryGetValue("annotations", out var outDir))
    {
        Console.Error.WriteLine("annotate needs the face model plus --photos <dir> and --annotations <dir>.");
        return 2;
    }

    if (Path.GetFullPath(outDir).StartsWith(Path.GetFullPath(photosDir), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("--annotations must not be inside the photos folder (originals stay untouched).");
        return 2;
    }

    Directory.CreateDirectory(outDir);
    var loader = new WicAnalysisImageLoader();
    var settings = AnalysisSettings.Default;
    var minScore = (float)AiThresholds.Default.FaceMinScore;
    var csv = new StringBuilder("file,faces_counted,detections,scores,face_sharpness,photo_sharpness,ms\n");
    var files = Directory.EnumerateFiles(photosDir).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).Order().ToList();
    var histogram = new SortedDictionary<int, int>();
    int failed = 0, n = 0;
    var total = Stopwatch.StartNew();
    foreach (var file in files)
    {
        n++;
        try
        {
            var image = loader.Load(file, settings.MaxPixelSize);
            var sw = Stopwatch.StartNew();
            var faces = detector.Detect(image, 0.5f).OrderBy(f => f.X).ToList();
            var ms = sw.Elapsed.TotalMilliseconds;
            var counted = faces.Count(f => f.Score >= minScore);
            histogram[counted] = histogram.GetValueOrDefault(counted) + 1;
            var sharp = faces.Select(f => AiAnalysisService.FaceSharpness(image, f) is { } v ? v.ToString("0", culture) : "-");
            csv.AppendLine(culture, $"{Path.GetFileName(file)},{counted},{faces.Count},{string.Join(' ', faces.Select(f => f.Score.ToString("0.00", culture)))},{string.Join(' ', sharp)},{SharpnessAnalyzer.Analyze(image, settings.SharpnessGrid).Score:0},{ms:0.0}");
            Annotator.Save(image, faces, minScore, Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + $"_f{counted}.jpg"));
        }
        catch (Exception ex) when (ex is PhotoReadException or IOException or UnauthorizedAccessException)
        {
            failed++;
            csv.AppendLine(culture, $"{Path.GetFileName(file)},error,,,,,");
        }

        if (n % 100 == 0)
        {
            Console.Error.WriteLine($"{n} / {files.Count}");
        }
    }

    File.WriteAllText(Path.Combine(outDir, "faces.csv"), csv.ToString());
    report.AppendLine(culture, $"{files.Count} photos, {failed} unreadable, {total.Elapsed.TotalSeconds:0} s (decode at {settings.MaxPixelSize} px + detection).");
    report.AppendLine();
    report.AppendLine(culture, $"Photos by number of faces counted (score ≥ {minScore:0.00}):");
    foreach (var (faces, count) in histogram)
    {
        report.AppendLine(culture, $"- {faces} face(s): {count}");
    }

    report.AppendLine();
    report.AppendLine("Green boxes = counted faces; amber = detections between 0.50 and the threshold; red dots = landmarks.");
    report.AppendLine("This is a visual check, not a measured recall: inspect the annotated copies.");
    return 0;
}

int BuildSheet(string kind, Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("photos", out var photosDir) || !opts.TryGetValue("sheet", out var sheetDir))
    {
        Console.Error.WriteLine(kind + " needs --photos <dir> and --sheet <new dir>.");
        return 2;
    }

    if (FrameForge.Evaluation.Sheets.JudgingSheets.CheckFolders(photosDir, sheetDir) is { } problem)
    {
        Console.Error.WriteLine(problem);
        return 2;
    }

    if (kind is "crop-sheet" or "mask-sheet" && detector is null)
    {
        Console.Error.WriteLine(kind + " needs the face model in --models.");
        return 2;
    }

    var count = int.Parse(opts.GetValueOrDefault("count", "30"), culture);
    var seed = int.Parse(opts.GetValueOrDefault("seed", "1"), culture);
    Directory.CreateDirectory(sheetDir);
    if (kind == "draw-sheet")
    {
        if (modelsDir is null)
        {
            Console.Error.WriteLine("draw-sheet needs --models <dir>.");
            return 2;
        }

        HashSet<string> drawExclude = new(StringComparer.OrdinalIgnoreCase);
        foreach (var excludeSheet in opts.GetValueOrDefault("exclude", string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            drawExclude.UnionWith((FrameForge.Evaluation.Sheets.JudgingSheets.ReadSheet(excludeSheet).Draw ?? []).Select(m => m.File));
        }

        var drawn = FrameForge.Evaluation.Sheets.DrawSheet.Build(modelsDir, photosDir, sheetDir, int.Parse(opts.GetValueOrDefault("count", "12"), culture), seed, drawExclude, report);
        if (drawn != 0)
        {
            return drawn;
        }
    }
    else if (kind == "sky-sheet")
    {
        HashSet<string> skyExclude = new(StringComparer.OrdinalIgnoreCase);
        foreach (var excludeSheet in opts.GetValueOrDefault("exclude", string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            skyExclude.UnionWith((FrameForge.Evaluation.Sheets.JudgingSheets.ReadSheet(excludeSheet).Sky ?? []).Select(m => m.File));
        }

        FrameForge.Evaluation.Sheets.SkySheet.Build(photosDir, sheetDir, count, seed, skyExclude, report);
    }
    else if (kind == "mask-sheet")
    {
        // Round 2 (default): people found by YOLOX-s prompt MobileSAM. Round 1: faces prompt MobileSAM, against MODNet.
        // Round 3: the app's subject mask with padded boxes against unpadded ones. --exclude takes sheets separated by ';'.
        HashSet<string> exclude = new(StringComparer.OrdinalIgnoreCase);
        foreach (var excludeSheet in opts.GetValueOrDefault("exclude", string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var previous = FrameForge.Evaluation.Sheets.JudgingSheets.ReadSheet(excludeSheet);
            exclude.UnionWith((previous.Masks ?? []).Select(m => m.File));
            exclude.UnionWith((previous.People ?? []).Select(m => m.File));
            exclude.UnionWith((previous.Padding ?? []).Select(m => m.File));
        }

        var built = opts.GetValueOrDefault("round", "2") switch
        {
            "1" => FrameForge.Evaluation.Sheets.MaskSheet.Build(modelsDir!, photosDir, sheetDir, count, seed, detector!, report),
            "3" => FrameForge.Evaluation.Sheets.PaddingSheet.Build(modelsDir!, photosDir, sheetDir, count, seed, exclude, report),
            _ => FrameForge.Evaluation.Sheets.MaskSheet.BuildPeople(modelsDir!, photosDir, sheetDir, count, seed, exclude, report),
        };
        if (built != 0)
        {
            return built;
        }
    }
    else if (kind == "light-sheet")
    {
        FrameForge.Evaluation.Sheets.JudgingSheets.BuildLight(photosDir, sheetDir, count, seed, report);
    }
    else
    {
        FrameForge.Evaluation.Sheets.JudgingSheets.BuildCrop(photosDir, sheetDir, count, seed, detector!, report);
    }

    report.AppendLine();
    report.AppendLine(culture, $"Open {Path.GetFullPath(Path.Combine(sheetDir, "index.html"))} in a browser, answer every item, press Save answers, then run score-sheet with the saved CSV.");
    return 0;
}

int BrandCommand(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("xaml", out var xaml) || !opts.TryGetValue("dir", out var outDir))
    {
        Console.Error.WriteLine("brand needs --xaml <Brand.xaml> and --dir <output dir>.");
        return 2;
    }

    return FrameForge.Evaluation.Brand.BrandAssets.Run(xaml, outDir, report);
}

int PerfCommand(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("work", out var work))
    {
        Console.Error.WriteLine("perf needs --work <new dir>.");
        return 2;
    }

    var sizeText = opts.GetValueOrDefault("sizes", "1000,10000,50000");
    var sizes = sizeText == "none" ? [] : sizeText.Split(',').Select(s => int.Parse(s, culture)).ToList();
    return FrameForge.Evaluation.Perf.PerfBench.RunAsync(work, sizes, opts.GetValueOrDefault("photos"), int.Parse(opts.GetValueOrDefault("count", "30"), culture), report).GetAwaiter().GetResult();
}

int RawSpikeCommand(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("raw", out var rawDir) || !opts.TryGetValue("libraw", out var dll) || !opts.TryGetValue("out-dir", out var outDir))
    {
        Console.Error.WriteLine("raw-spike needs --raw <dir>, --libraw <libraw.dll> and --out-dir <new dir>.");
        return 2;
    }

    return FrameForge.Evaluation.Raw.RawSpike.Run(rawDir, opts.GetValueOrDefault("jpeg"), dll, outDir, int.Parse(opts.GetValueOrDefault("count", "20"), culture), report);
}

int SkyDebugCommand(Dictionary<string, string> opts)
{
    // Developer use: sky-debug --photo <file> --picture <out.jpg>
    if (opts.TryGetValue("picture", out var picture))
    {
        FrameForge.Evaluation.Sheets.SkyDebug.Write(opts["photo"], picture);
    }

    if (opts.TryGetValue("overlays", out var overlays))
    {
        FrameForge.Evaluation.Sheets.SkyOverlays.Write(opts["photo"].Split(';', StringSplitOptions.RemoveEmptyEntries), overlays);
        return 0;
    }

    foreach (var photo in opts["photo"].Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        FrameForge.Evaluation.Sheets.SkyStats.Write(photo, report);
    }

    return 0;
}

int MaskBenchmarkCommand(Dictionary<string, string> opts)
{
    if (modelsDir is null || !opts.TryGetValue("photos", out var photosDir))
    {
        Console.Error.WriteLine("mask-benchmark needs --models <dir> and --photos <dir>.");
        return 2;
    }

    return FrameForge.Evaluation.Masks.MaskBenchmark.Run(
        modelsDir, photosDir, int.Parse(opts.GetValueOrDefault("count", "20"), culture), int.Parse(opts.GetValueOrDefault("seed", "1"), culture),
        opts.GetValueOrDefault("threads", "2,4,6").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), report);
}

int ScoreSheet(Dictionary<string, string> opts)
{
    if (!opts.TryGetValue("sheet", out var sheetDir) || !opts.TryGetValue("answers", out var answersPath))
    {
        Console.Error.WriteLine("score-sheet needs --sheet <dir> and --answers <saved csv>.");
        return 2;
    }

    if (!File.Exists(answersPath))
    {
        Console.Error.WriteLine("No answers file at " + answersPath + ". Open the sheet's index.html, judge every item, press Save answers, then score the file it saves (usually in Downloads).");
        return 2;
    }

    try
    {
        return FrameForge.Evaluation.Sheets.JudgingSheets.Score(sheetDir, answersPath, report);
    }
    catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine("Could not score: " + ex.Message);
        return 1;
    }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        result[args[i].TrimStart('-')] = args[i + 1];
    }

    return result;
}

/// <summary>Serves model files from a folder, accepting only files whose SHA-256 matches the catalogue.</summary>
internal sealed class FolderModelStore(string folder) : IModelStore
{
    private readonly Dictionary<string, ModelStatus> _cache = [];

    public ModelStatus GetStatus(ModelDescriptor model)
    {
        if (_cache.TryGetValue(model.Id, out var cached))
        {
            return cached;
        }

        var match = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.onnx").FirstOrDefault(f => new FileInfo(f).Length == model.SizeBytes && Sha256(f) == model.Sha256)
            : null;
        return _cache[model.Id] = new ModelStatus(model, match is null ? ModelState.NotInstalled : ModelState.Installed, match);
    }

    public Task<string> DownloadAsync(ModelDescriptor model, ModelDownloadConsent consent, IProgress<double>? progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The evaluation tool never downloads models.");

    public Task<string> ImportAsync(ModelDescriptor model, string sourceFile, CancellationToken cancellationToken) => throw new NotSupportedException();

    public void Remove(ModelDescriptor model) => throw new NotSupportedException();

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
