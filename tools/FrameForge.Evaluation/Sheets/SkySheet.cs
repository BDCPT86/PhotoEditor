using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Application.Editing.Masks;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using FrameForge.Rendering;
using MaskMath = FrameForge.Evaluation.Masks.MaskMath;

namespace FrameForge.Evaluation.Sheets;

/// <summary>One photo on the sky sheet: whether the rules found sky, and how much.</summary>
internal sealed record SkyItem(string Id, string File, bool Found, string Summary, double Coverage, double Ms, string Source);

/// <summary>
/// Milestone 08 phase B, B7: grades the app's <see cref="SkyMaskGenerator"/> (no model) on a random sample of photos.
/// About two thirds are photos where the rules found sky, the rest photos where they found none, so both false sky
/// and missed sky are seen. Photos where sky was found are graded Good / Usable / Bad / No real sky; photos where none
/// was found are graded Right (no sky) / Missed.
/// </summary>
internal static class SkySheet
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The answers a sky sheet accepts.</summary>
    internal static readonly string[] FoundGrades = ["good", "usable", "bad", "nosky"];

    internal static readonly string[] NotFoundGrades = ["right", "missed"];

    public static int Build(string photosDir, string outDir, int count, int seed, IReadOnlySet<string> excludeFiles, StringBuilder report)
    {
        var sky = new SkyMaskGenerator();
        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var id = "sky-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        int wantFound = (int)Math.Round(count * 2 / 3.0), wantNotFound = count - wantFound;
        var items = new List<SkyItem>();
        int found = 0, notFound = 0, unreadable = 0, excluded = 0, seen = 0, seenFound = 0;

        foreach (var file in JudgingSheets.Sample(Directory.EnumerateFiles(photosDir).Where(IsJpeg), seed))
        {
            if (found >= wantFound && notFound >= wantNotFound)
            {
                break;
            }

            if (excludeFiles.Contains(Path.GetFileName(file)))
            {
                excluded++;
                continue;
            }

            AnalysisImage image;
            try
            {
                image = loader.Load(file, MaskService.AnalysisLongSide);
            }
            catch (PhotoReadException)
            {
                unreadable++;
                continue;
            }

            seen++;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = sky.Generate(image, [], CancellationToken.None);
            var ms = sw.Elapsed.TotalMilliseconds;
            seenFound += result.HasMask ? 1 : 0;
            if (result.HasMask ? found >= wantFound : notFound >= wantNotFound)
            {
                continue;
            }

            var itemId = "s" + (items.Count + 1).ToString("000", Culture);
            var display = loader.Load(file, JudgingSheets.DisplayLongSide);
            Save(encoder, display, Path.Combine(img, itemId + "-photo.jpg"));
            if (result.HasMask)
            {
                found++;
                Save(encoder, View(display, result.Mask!), Path.Combine(img, itemId + "-mask.jpg"));
            }
            else
            {
                notFound++;
            }

            items.Add(new SkyItem(itemId, Path.GetFileName(file), result.HasMask, result.Summary, Math.Round(result.Mask?.Coverage() ?? 0, 4), Math.Round(ms, 1), sky.Source));
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();

        // Shuffle so found and not-found photos are mixed (seeded, so a rebuild gives the same order).
        var random = new Random(seed);
        var order = items.OrderBy(_ => random.Next()).ToList();
        var sheet = new JudgingSheets.SheetFile("mask-sky", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), 0, null, null, Sky: order);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new
        {
            id,
            items = order.Select(i => new { id = i.Id, found = i.Found, photo = "img/" + i.Id + "-photo.jpg", mask = i.Found ? "img/" + i.Id + "-mask.jpg" : null }),
        });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(Page, pageData));

        report.AppendLine(Culture, $"Sky sheet `{id}` ({sky.Source}): {items.Count} photos, sky found on {found} and not on {notFound} (seed {seed}).");
        report.AppendLine(Culture, $"Of the {seen} photos checked on the way, the rules found sky on {seenFound} ({(seen == 0 ? 0 : seenFound / (double)seen):P0}); skipped {unreadable} unreadable and {excluded} from earlier sheets.");
        if (items.Count > 0)
        {
            report.AppendLine(Culture, $"- Sky mask time, median: {items.Select(i => i.Ms).Order().ElementAt(items.Count / 2):0} ms (1024 px).");
        }

        return 0;
    }

    public static int Score(JudgingSheets.SheetFile sheet, Dictionary<string, string[]> raw, StringBuilder report)
    {
        var items = sheet.Sky ?? [];
        var rows = new List<(SkyItem Item, string Grade)>();
        foreach (var item in items)
        {
            if (!raw.TryGetValue(item.Id, out var answer))
            {
                continue;
            }

            var grade = answer[0].Trim().ToLowerInvariant();
            if (!(item.Found ? FoundGrades : NotFoundGrades).Contains(grade))
            {
                throw new FormatException($"Unknown answer for {item.Id}: \"{answer[0]}\".");
            }

            rows.Add((item, grade));
        }

        var found = rows.Where(r => r.Item.Found).ToList();
        var notFound = rows.Where(r => !r.Item.Found).ToList();
        int good = Count(found, "good"), usable = Count(found, "usable"), bad = Count(found, "bad"), noSky = Count(found, "nosky");
        int right = Count(notFound, "right"), missed = Count(notFound, "missed");

        report.AppendLine(Culture, $"## Sky masks ({items.FirstOrDefault()?.Source ?? "?"}) — sheet `{sheet.Id}`");
        report.AppendLine();
        report.AppendLine(Culture, $"- Photos answered: {rows.Count} of {items.Count}");
        var (low, high) = SheetScoring.Wilson(good + usable, found.Count);
        report.AppendLine(Culture, $"- **Sky found ({found.Count}):** good {good} · usable {usable} · bad {bad} · no real sky {noSky}; good or usable {Share(good + usable, found.Count)} (95 % interval {low:P0}–{high:P0})");
        var (fpLow, fpHigh) = SheetScoring.Wilson(noSky, found.Count);
        report.AppendLine(Culture, $"- **False sky** (found on a photo with no real sky): {noSky} of {found.Count}, {Share(noSky, found.Count)} (95 % interval {fpLow:P0}–{fpHigh:P0})");
        report.AppendLine(Culture, $"- **No sky found ({notFound.Count}):** right {right} · missed visible sky {missed}");
        report.AppendLine();
        report.AppendLine("| Photo | File | Rules said | Sky share | Grade |");
        report.AppendLine("|---|---|---|---:|---|");
        foreach (var (item, grade) in rows)
        {
            report.AppendLine(Culture, $"| {item.Id} | {item.File} | {item.Summary} | {item.Coverage:P1} | {grade} |");
        }

        report.AppendLine();
        report.AppendLine("Grades from one person on one set of photos; the sample is two thirds photos where sky was found, so these are not rates for a whole library.");
        return 0;

        static int Count(List<(SkyItem Item, string Grade)> list, string grade) => list.Count(r => r.Grade == grade);
        static string Share(int part, int whole) => whole == 0 ? "–" : (part / (double)whole).ToString("P0", Culture);
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
        encoder.Write(image, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 90);
    }

    private const string Page = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Sky masks</title>
        """ + SheetPages.Style + """
        <style>
        .stage{display:flex;flex-direction:column;align-items:center;gap:10px;padding:10px 18px 0}
        .stage img{max-width:calc(100vw - 36px);max-height:calc(100vh - 260px);display:block;border-radius:2px;cursor:zoom-in}
        .legend{text-align:center;margin:10px 18px 0;font-size:13px}
        .verdict{text-align:center;font-weight:600;margin:6px 0 0}
        </style></head><body>
        <header><h1>Sky masks</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted legend">When sky was found, it is lit and everything else is dark, with an amber edge.
        <b>Good</b> = use as is · <b>Usable</b> = use after a click or two · <b>Bad</b> = big parts missing or much that isn't sky · <b>No real sky</b> = the photo has no sky, or the mask is only on things that aren't sky.<br>
        When no sky was found, you see the photo: <b>Right</b> = there is no sky worth editing · <b>Missed</b> = there is visible sky.
        Hold <kbd>Space</kbd> to see the photo; click the picture to open it full size.</p>
        <main id="main"></main>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        const FOUND = [["good", "Good", "1"], ["usable", "Usable", "2"], ["bad", "Bad", "3"], ["nosky", "No real sky", "4"]];
        const NOTFOUND = [["right", "Right, no sky", "1"], ["missed", "Missed visible sky", "3"]];
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        let index = data.items.findIndex(i => !answers[i.id]); if (index < 0) index = data.items.length;
        let peek = false;
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const answered = () => data.items.filter(i => answers[i.id]).length;
        const grades = item => item.found ? FOUND : NOTFOUND;
        function render() {
          document.getElementById("progress").textContent = answered() + " of " + data.items.length + " graded";
          const main = document.getElementById("main");
          if (index >= data.items.length) {
            main.innerHTML = '<div class="done"><h2>All photos graded</h2><p class="muted">Save your answers, then run <code>score-sheet</code> with the saved file. <kbd>Backspace</kbd> goes back to change any grade.</p><button id="save2">Save answers</button></div>';
            document.getElementById("save2").onclick = save; return;
          }
          const item = data.items[index], chosen = answers[item.id];
          const src = item.found && !peek ? item.mask : item.photo;
          main.innerHTML = '<p class="verdict">' + (item.found ? "Sky found" : "No sky found") + '</p>' +
            '<div class="stage"><a href="' + src + '" target="_blank" rel="noopener"><img src="' + src + '" alt="' + (item.found ? "Sky mask" : "Photo") + '"></a></div>' +
            '<div class="bar">' + grades(item).map(([g, label, k]) => '<button data-g="' + g + '"' + (chosen === g ? ' class="chosen"' : "") + '>' + label + ' <kbd>' + k + '</kbd></button>').join("") +
            '<button id="back">Back <kbd>Backspace</kbd></button><span class="muted" style="margin-left:16px">Photo ' + (index + 1) + '</span></div>';
          main.querySelectorAll("[data-g]").forEach(b => b.onclick = () => grade(b.dataset.g));
          document.getElementById("back").onclick = back;
          const n = data.items[index + 1]; if (n) { new Image().src = n.found ? n.mask : n.photo; }
        }
        function grade(g) { if (index >= data.items.length) return; answers[data.items[index].id] = g; persist(); index++; render(); }
        function back() { index = Math.max(0, index - 1); render(); }
        function save() {
          const lines = ["id,answer"].concat(data.items.filter(i => answers[i.id]).map(i => i.id + "," + answers[i.id]));
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([lines.join("\n") + "\n"], { type: "text/csv" }));
          a.download = "sky-answers-" + data.id + ".csv"; a.click();
        }
        document.getElementById("save").onclick = save;
        document.addEventListener("keydown", e => {
          if (index < data.items.length) {
            const hit = grades(data.items[index]).find(([, , k]) => k === e.key);
            if (hit) { grade(hit[0]); e.preventDefault(); return; }
          }
          if (e.key === "Backspace") back();
          else if (e.key === " ") { if (!peek) { peek = true; render(); } }
          else return;
          e.preventDefault();
        });
        document.addEventListener("keyup", e => { if (e.key === " ") { peek = false; render(); } });
        render();
        </script></body></html>
        """;
}

/// <summary>A picture of what the sky rules decide for each pixel, for tuning them (developer use only).</summary>
internal static class SkyDebug
{
    /// <summary>
    /// Black: not a sky colour. Dark red: sky colour but not smooth. Grey: smooth sky colour that wasn't reached.
    /// The photo at full brightness: selected as sky.
    /// </summary>
    public static void Write(string photo, string outPath)
    {
        var loader = new WicAnalysisImageLoader();
        var image = loader.Load(photo, MaskService.AnalysisLongSide);
        var (_, candidate, smooth) = SkyMaskGenerator.Classify(image, CancellationToken.None);
        var result = new SkyMaskGenerator().Generate(image, [], CancellationToken.None);
        var px = image.Bgra;
        var view = AnalysisImage.Create(image.Width, image.Height, (x, y) =>
        {
            var i = (y * image.Width) + x;
            if (result.Mask is { } m && m.Alpha[i] >= 128)
            {
                return (px[(i * 4) + 2], px[(i * 4) + 1], px[i * 4]);
            }

            return !candidate[i] ? ((byte)0, (byte)0, (byte)0) : smooth[i] ? ((byte)110, (byte)110, (byte)110) : ((byte)120, (byte)20, (byte)20);
        });
        using var stream = File.Create(outPath);
        new WicJpegEncoder().Write(view, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 90);
        Console.WriteLine(result.Summary);
    }
}

/// <summary>Statistics of each connected part of the sky mask, for tuning the rules (developer use only).</summary>
internal static class SkyStats
{
    public static void Write(string photo, StringBuilder report)
    {
        var image = new WicAnalysisImageLoader().Load(photo, MaskService.AnalysisLongSide);
        int w = image.Width, h = image.Height;
        var result = new SkyMaskGenerator().Generate(image, [], CancellationToken.None);
        report.AppendLine(CultureInfo.InvariantCulture, $"### {Path.GetFileName(photo)}: {result.Summary}");
        if (result.Mask is not { } mask)
        {
            return;
        }

        var (luma, _, _) = SkyMaskGenerator.Classify(image, CancellationToken.None);
        var std = SkyMaskGenerator.LocalTexture(luma, w, h, 2);
        var px = image.Bgra;
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        report.AppendLine("| part | area % | bbox x0-x1, y0-y1 (%) | mean luma | p10 luma | mean b−r | mean spread | median std | p90 std |");
        report.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|");
        var part = 0;
        for (var start = 0; start < w * h; start++)
        {
            if (seen[start] || mask.Alpha[start] < 128)
            {
                continue;
            }

            var pixels = new List<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                pixels.Add(i);
                int x = i % w, y = i / w;
                foreach (var j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                {
                    if (j >= 0 && !seen[j] && mask.Alpha[j] >= 128)
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (pixels.Count < w * h / 1000)
            {
                continue;
            }

            part++;
            var lumas = pixels.Select(i => luma[i]).Order().ToList();
            var stds = pixels.Select(i => std[i]).Order().ToList();
            double br = pixels.Average(i => (px[i * 4] - px[(i * 4) + 2]) / 255.0);
            double spread = pixels.Average(i => (Math.Max(px[i * 4], Math.Max(px[(i * 4) + 1], px[(i * 4) + 2])) - Math.Min(px[i * 4], Math.Min(px[(i * 4) + 1], px[(i * 4) + 2]))) / 255.0);
            int x0 = pixels.Min(i => i % w), x1 = pixels.Max(i => i % w), y0 = pixels.Min(i => i / w), y1 = pixels.Max(i => i / w);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {part} | {pixels.Count * 100.0 / (w * h):0.0} | {x0 * 100 / w}-{x1 * 100 / w}, {y0 * 100 / h}-{y1 * 100 / h} | {lumas.Average():0.00} | {lumas[lumas.Count / 10]:0.00} | {br:0.000} | {spread:0.000} | {stds[stds.Count / 2]:0.0000} | {stds[stds.Count * 9 / 10]:0.0000} |");
        }
    }

}

/// <summary>Sky mask overlays for a list of photos, to look at a change of rules (developer use only).</summary>
internal static class SkyOverlays
{
    public static void Write(IEnumerable<string> photos, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var sky = new SkyMaskGenerator();
        foreach (var photo in photos)
        {
            var image = loader.Load(photo, MaskService.AnalysisLongSide);
            var result = sky.Generate(image, [], CancellationToken.None);
            var view = result.Mask is { } m ? MaskMath.Overlay(image, m.Alpha.Select(a => a / 255f).ToArray()) : image;
            using var stream = File.Create(Path.Combine(outDir, Path.GetFileNameWithoutExtension(photo) + ".jpg"));
            encoder.Write(view, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 85);
            Console.WriteLine(Path.GetFileName(photo) + ": " + result.Summary);
        }
    }
}
