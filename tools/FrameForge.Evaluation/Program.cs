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
// <dir> holds the catalogued model files (verified by SHA-256 before use). Photos are opened read-only.
var culture = CultureInfo.InvariantCulture;
if (args.Length == 0 || args[0] is not ("benchmark" or "evaluate" or "annotate"))
{
    Console.Error.WriteLine("Usage:\n  benchmark --models <dir> [--count 100] [--out report.md]\n  evaluate --models <dir> --photos <dir> --labels <labels.csv> [--out report.md]\n  annotate --models <dir> --photos <dir> --annotations <dir>");
    return 2;
}

var options = ParseOptions(args.Skip(1).ToArray());
if (!options.TryGetValue("models", out var modelsDir))
{
    Console.Error.WriteLine("--models <dir> is required.");
    return 2;
}

var store = new FolderModelStore(modelsDir);
using var runtime = new OnnxAiRuntime(store, NullLogger<OnnxAiRuntime>.Instance);
var detector = runtime.GetFaceDetector();
var embedder = runtime.GetImageEmbedder();
if (detector is null && embedder is null)
{
    Console.Error.WriteLine("No verified model found in " + modelsDir + (runtime.LastError is { } e ? ": " + e : "."));
    return 1;
}

var report = new StringBuilder();
report.AppendLine(culture, $"# FrameForge AI {args[0]} — {DateTime.Now:yyyy-MM-dd HH:mm}");
report.AppendLine();
report.AppendLine(culture, $"- Machine: {Environment.ProcessorCount} logical CPUs · {Environment.OSVersion} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
report.AppendLine(culture, $"- Runtime: ONNX Runtime CPU, {OnnxAiRuntime.IntraOpThreads} intra-op threads per session, one photo at a time");
report.AppendLine(culture, $"- Face detector: {detector?.VersionTag ?? "not installed"}");
report.AppendLine(culture, $"- Scene embedder: {embedder?.VersionTag ?? "not installed"}");
report.AppendLine();

var exit = args[0] switch
{
    "benchmark" => Benchmark(int.Parse(options.GetValueOrDefault("count", "100"), culture)),
    "annotate" => Annotate(options),
    _ => Evaluate(options),
};

var text = report.ToString();
Console.WriteLine(text);
if (options.TryGetValue("out", out var outPath))
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
