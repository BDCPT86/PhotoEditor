using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FrameForge.Analysis;
using FrameForge.Application.Editing.Masks;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Evaluation.Sheets;
using FrameForge.Imaging;
using FrameForge.Rendering;

namespace FrameForge.Evaluation.Masks;

/// <summary>
/// Milestone 08 phase B, B7: how long each part of a mask takes on real photos, with the app's own classes, and how
/// much memory the process uses. Every thread count runs on every photo, in a rotating order, so a change in the
/// machine's load during the run affects all of them alike (back-to-back runs on a laptop differed by 3× for that
/// reason alone). Photos are opened read-only and nothing is written except the report.
/// </summary>
internal static class MaskBenchmark
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static int Run(string modelsDir, string photosDir, int count, int seed, IReadOnlyList<string> threads, StringBuilder report)
    {
        var paths = new[] { MaskCandidates.PersonDetector, MaskCandidates.SamEncoder, MaskCandidates.SamDecoder }.Select(m => MaskCandidates.Find(modelsDir, m)).ToList();
        if (paths.Any(p => p is null))
        {
            Console.Error.WriteLine("mask-benchmark needs object_detection_yolox_2022nov.onnx, mobile_sam.encoder.onnx and mobile_sam.decoder.onnx (exact SHA-256) in --models.");
            return 2;
        }

        var files = JudgingSheets.Sample(Directory.EnumerateFiles(photosDir).Where(IsJpeg), seed).Take(count + 1).ToList();
        if (files.Count < 2)
        {
            Console.Error.WriteLine("mask-benchmark needs at least two JPEG photos in " + photosDir + ".");
            return 2;
        }

        // The last sampled photo is only decoded alongside masks, to measure overlapping the next photo's decode.
        var extra = files[^1];
        files.RemoveAt(files.Count - 1);
        var loader = new WicAnalysisImageLoader();
        var configs = new List<Config>();
        foreach (var label in threads)
        {
            // "4" = 4 threads; "4n" = 4 threads with spinning off.
            var spin = !label.EndsWith('n');
            var t = int.Parse(label.TrimEnd('n'), Culture);
            var load = Stopwatch.StartNew();
            var runtime = new FolderMaskRuntime(paths[0]!, paths[1]!, paths[2]!, t, spin);
            configs.Add(new Config(t + (spin ? " threads" : " threads, no spin"), runtime, new SubjectMaskGenerator(runtime), load.Elapsed.TotalMilliseconds));
        }

        try
        {
            // Warm-up: the first run of each session includes one-off set-up; keep it out of the timings.
            foreach (var c in configs)
            {
                var sw = Stopwatch.StartNew();
                c.Subject.Generate(loader.Load(files[0], MaskService.AnalysisLongSide), [], CancellationToken.None);
                c.WarmMs = sw.Elapsed.TotalMilliseconds;
            }

            // Memory with the models loaded and masks made, before any full-size photo is decoded.
            long masksPeak, renderAllocated = 0;
            using (var before = Process.GetCurrentProcess())
            {
                masksPeak = before.PeakWorkingSet64;
            }

            var sky = new SkyMaskGenerator();
            var shared = new List<SharedRow>();
            for (var p = 0; p < files.Count; p++)
            {
                var file = files[p];
                var sw = Stopwatch.StartNew();
                AnalysisImage image;
                try
                {
                    image = loader.Load(file, MaskService.AnalysisLongSide);
                }
                catch (PhotoReadException)
                {
                    continue;
                }

                var decodeMs = sw.Elapsed.TotalMilliseconds;
                string summary = string.Empty;
                MaskImage? subjectMask = null;
                for (var k = 0; k < configs.Count; k++)
                {
                    var c = configs[(p + k) % configs.Count];
                    var copy = loader.Load(file, MaskService.AnalysisLongSide);   // a fresh image, so no cache is reused
                    sw.Restart();
                    c.Runtime.GetPersonDetector()!.Detect(copy, 0.5f, CancellationToken.None);
                    var detectMs = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    var mask = c.Subject.Generate(copy, [], CancellationToken.None);
                    var subjectMs = sw.Elapsed.TotalMilliseconds;
                    double? decoderMs = null, overlappedMs = null;
                    if (mask.HasMask)
                    {
                        // The decoder alone, as for a click: the people and the encoding are reused.
                        sw.Restart();
                        c.Subject.Generate(copy, [], CancellationToken.None);
                        decoderMs = sw.Elapsed.TotalMilliseconds;

                        // The whole mask again on a fresh image while another photo decodes alongside (batch overlap).
                        var again = loader.Load(file, MaskService.AnalysisLongSide);
                        sw.Restart();
                        var decode = Task.Run(() => loader.Load(extra, MaskService.AnalysisLongSide));
                        c.Subject.Generate(again, [], CancellationToken.None);
                        decode.GetAwaiter().GetResult();
                        overlappedMs = sw.Elapsed.TotalMilliseconds;
                        subjectMask ??= mask.Mask;
                    }

                    summary = mask.Summary;
                    c.Rows.Add(new ThreadRow(mask.HasMask, detectMs, subjectMs, decoderMs, overlappedMs, decodeMs));
                }

                sw.Restart();
                sky.Generate(image, [], CancellationToken.None);
                var skyMs = sw.Elapsed.TotalMilliseconds;

                // Export: full-size decode and a render with one masked adjustment (the subject).
                sw.Restart();
                var full = loader.Load(file, int.MaxValue);
                var fullDecodeMs = sw.Elapsed.TotalMilliseconds;
                var local = new LocalAdjustment
                {
                    Id = Guid.NewGuid(),
                    Mask = new MaskReference { Kind = MaskKind.Subject, Source = configs[0].Subject.Source, Points = ValueList.Empty<MaskPoint>() },
                    Exposure = 0.5,
                };
                var recipe = EditRecipe.Neutral with { Exposure = 0.2, LocalAdjustments = new([local]) };
                double? renderMs = null;
                if (subjectMask is not null)
                {
                    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                    sw.Restart();
                    EditRenderer.Render(full, recipe, new Dictionary<Guid, MaskImage> { [local.Id] = subjectMask });
                    renderMs = sw.Elapsed.TotalMilliseconds;
                    renderAllocated = Math.Max(renderAllocated, GC.GetTotalAllocatedBytes(true) - allocatedBefore);
                }

                sw.Restart();
                EditRenderer.Render(full, EditRecipe.Neutral with { Exposure = 0.2 });
                var renderPlainMs = sw.Elapsed.TotalMilliseconds;
                shared.Add(new SharedRow(Path.GetFileName(file), full.Width * full.Height / 1e6, summary, decodeMs, skyMs, fullDecodeMs, renderMs, renderPlainMs));
                Console.Error.Write('.');
            }

            Console.Error.WriteLine();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            Write(report, seed, configs, shared, process.PeakWorkingSet64, masksPeak, renderAllocated);
            return 0;
        }
        finally
        {
            foreach (var c in configs)
            {
                c.Runtime.Dispose();
            }
        }
    }

    private static void Write(StringBuilder report, int seed, List<Config> configs, List<SharedRow> shared, long peakWorkingSet, long masksPeak, long renderAllocated)
    {
        report.AppendLine(Culture, $"## Masks on {shared.Count} photos (seed {seed}), settings {string.Join("; ", configs.Select(c => c.Label))} interleaved");
        report.AppendLine();
        report.AppendLine(Culture, $"- Photos: median {Median(shared.Select(r => r.Megapixels)):0.0} MP; a subject was found in {configs[0].Rows.Count(r => r.HasMask)} of {shared.Count}.");
        report.AppendLine(Culture, $"- Peak memory (whole process, working set): {masksPeak / 1048576d:0} MB with {configs.Count} {(configs.Count == 1 ? "copy" : "copies")} of the mask models loaded and masks made at 1024 px; {peakWorkingSet / 1048576d:0} MB after the full-size decodes and renders. A full-size render with one mask allocated up to {renderAllocated / 1048576d:0} MB.");
        foreach (var c in configs)
        {
            report.AppendLine(Culture, $"- {c.Label}: loading the three models {c.LoadMs:0} ms, first mask (one-off set-up) {c.WarmMs:0} ms.");
        }

        report.AppendLine();
        report.AppendLine("Median (90th percentile) in ms, photos with people:");
        report.AppendLine();
        report.AppendLine("| Stage | " + string.Join(" | ", configs.Select(c => c.Label)) + " |");
        report.AppendLine("|---|" + string.Concat(configs.Select(_ => "---:|")));
        Line("Find people (YOLOX-s)", r => r.DetectMs);
        Line("**Subject mask** (find people, encode, decode)", r => r.SubjectMs);
        Line("Decoder only (a click)", r => r.DecoderMs!.Value);
        Line("Encoder, estimated (subject − find people − decoder)", r => r.SubjectMs - r.DetectMs - r.DecoderMs!.Value);
        Line("Batch step, one after another (decode at 1024 px + subject)", r => r.DecodeMs + r.SubjectMs);
        Line("Batch step, next photo decoded alongside (subject while a decode runs)", r => r.OverlappedMs!.Value);
        report.AppendLine();
        report.AppendLine("Not affected by the thread count (median / 90th percentile ms):");
        report.AppendLine();
        report.AppendLine("| Stage | median | 90th pct |");
        report.AppendLine("|---|---:|---:|");
        Shared("Decode at 1024 px (analysis size)", shared.Select(r => r.DecodeMs));
        Shared("**Sky mask** (rules)", shared.Select(r => r.SkyMs));
        Shared("Full-size decode (export)", shared.Select(r => r.FullDecodeMs));
        Shared("Full-size render, global edit only", shared.Select(r => r.RenderPlainMs));
        Shared("Full-size render, plus one masked adjustment", shared.Where(r => r.RenderMs is not null).Select(r => r.RenderMs!.Value));
        report.AppendLine();
        report.AppendLine("| Photo | MP | Found | " + string.Join(" | ", configs.Select(c => "Subject ms, " + c.Label)) + " | Sky ms |");
        report.AppendLine("|---|---:|---|" + string.Concat(configs.Select(_ => "---:|")) + "---:|");
        for (var i = 0; i < shared.Count; i++)
        {
            var r = shared[i];
            report.AppendLine(Culture, $"| {r.File} | {r.Megapixels:0.0} | {r.Summary} | {string.Join(" | ", configs.Select(c => c.Rows[i].SubjectMs.ToString("0", Culture)))} | {r.SkyMs:0} |");
        }

        report.AppendLine();
        report.AppendLine("Speed figures only. Background load on the machine (other apps, power mode, temperature) changes them; the interleaving keeps the comparison between thread counts fair, not the absolute numbers.");

        void Line(string name, Func<ThreadRow, double> pick) =>
            report.AppendLine(Culture, $"| {name} | {string.Join(" | ", configs.Select(c => Cell(c.Rows.Where(r => r.HasMask).Select(pick))))} |");

        void Shared(string name, IEnumerable<double> values)
        {
            var list = values.Order().ToList();
            report.AppendLine(list.Count == 0 ? $"| {name} | – | – |" : string.Create(Culture, $"| {name} | {list[list.Count / 2]:0} | {P90(list):0} |"));
        }
    }

    private static string Cell(IEnumerable<double> values)
    {
        var list = values.Order().ToList();
        return list.Count == 0 ? "–" : string.Create(Culture, $"{list[list.Count / 2]:0} ({P90(list):0})");
    }

    private static double P90(List<double> sorted) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.9) - 1)];

    private static double Median(IEnumerable<double> values)
    {
        var list = values.Order().ToList();
        return list.Count == 0 ? 0 : list[list.Count / 2];
    }

    private static bool IsJpeg(string f) => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private sealed record ThreadRow(bool HasMask, double DetectMs, double SubjectMs, double? DecoderMs, double? OverlappedMs, double DecodeMs);

    private sealed record SharedRow(string File, double Megapixels, string Summary, double DecodeMs, double SkyMs, double FullDecodeMs, double? RenderMs, double RenderPlainMs);

    private sealed class Config(string label, FolderMaskRuntime runtime, SubjectMaskGenerator subject, double loadMs)
    {
        public string Label { get; } = label;

        public FolderMaskRuntime Runtime { get; } = runtime;

        public SubjectMaskGenerator Subject { get; } = subject;

        public double LoadMs { get; } = loadMs;

        public double WarmMs { get; set; }

        public List<ThreadRow> Rows { get; } = [];
    }
}
