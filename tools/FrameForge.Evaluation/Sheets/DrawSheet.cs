using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrameForge.Analysis;
using FrameForge.Application.Editing.Masks;
using FrameForge.Application.Photos;
using FrameForge.Imaging;
using FrameForge.Rendering;
using FolderMaskRuntime = FrameForge.Evaluation.Masks.FolderMaskRuntime;
using MaskCandidates = FrameForge.Evaluation.Masks.MaskCandidates;

namespace FrameForge.Evaluation.Sheets;

/// <summary>One photo on the drawing sheet, with what the app found (its masks are saved next to the sheet, not shown).</summary>
internal sealed record DrawItem(string Id, string File, int MaskWidth, int MaskHeight, bool SubjectFound, string SubjectSummary, bool SkyFound, string SkySummary);

/// <summary>One outline drawn on the page: points in 0–1 of the photo, adding to the area or cutting it out.</summary>
internal sealed record DrawnShape([property: JsonPropertyName("mode")] string Mode, [property: JsonPropertyName("pts")] double[][] Points);

/// <summary>Everything drawn on one photo.</summary>
internal sealed record DrawnPhoto(
    [property: JsonPropertyName("subject")] List<DrawnShape>? Subject,
    [property: JsonPropertyName("sky")] List<DrawnShape>? Sky,
    [property: JsonPropertyName("noSky")] bool NoSky,
    [property: JsonPropertyName("skipped")] bool Skipped);

/// <summary>
/// Milestone 08 phase B, B7: the brief's "mask IoU against hand-drawn masks". You outline the subject (and the sky,
/// if any) on photos without seeing the app's masks; <c>score-sheet</c> then compares the app's masks with yours:
/// overlap (IoU) and how closely the edges agree.
/// </summary>
internal static class DrawSheet
{
    /// <summary>Edge agreement counts an edge pixel as matched within this many pixels of the other edge (at 1024 px).</summary>
    internal const int EdgeTolerance = 3;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Build(string modelsDir, string photosDir, string outDir, int count, int seed, IReadOnlySet<string> excludeFiles, StringBuilder report)
    {
        var paths = new[] { MaskCandidates.PersonDetector, MaskCandidates.SamEncoder, MaskCandidates.SamDecoder }.Select(m => MaskCandidates.Find(modelsDir, m)).ToList();
        if (paths.Any(p => p is null))
        {
            Console.Error.WriteLine("draw-sheet needs object_detection_yolox_2022nov.onnx, mobile_sam.encoder.onnx and mobile_sam.decoder.onnx (exact SHA-256) in --models.");
            return 2;
        }

        using var runtime = new FolderMaskRuntime(paths[0]!, paths[1]!, paths[2]!);
        var subject = new SubjectMaskGenerator(runtime);
        var sky = new SkyMaskGenerator();
        var loader = new WicAnalysisImageLoader();
        var encoder = new WicJpegEncoder();
        var id = "draw-" + seed.ToString(Culture) + "-" + DateTime.Now.ToString("yyyyMMddHHmm", Culture);
        var img = Directory.CreateDirectory(Path.Combine(outDir, "img")).FullName;
        var masks = Directory.CreateDirectory(Path.Combine(outDir, "app-masks")).FullName;
        var items = new List<DrawItem>();
        int noPerson = 0, unreadable = 0, excluded = 0;

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

            // Only photos with people: the subject is the main thing being measured.
            var s = subject.Generate(image, [], CancellationToken.None);
            if (!s.HasMask)
            {
                noPerson++;
                continue;
            }

            var k = sky.Generate(image, [], CancellationToken.None);
            var itemId = "d" + (items.Count + 1).ToString("000", Culture);
            Save(encoder, loader.Load(file, JudgingSheets.DisplayLongSide), Path.Combine(img, itemId + "-photo.jpg"));
            WriteMask(Path.Combine(masks, itemId + "-subject.bin"), s.Mask!);
            if (k.HasMask)
            {
                WriteMask(Path.Combine(masks, itemId + "-sky.bin"), k.Mask!);
            }

            items.Add(new DrawItem(itemId, Path.GetFileName(file), image.Width, image.Height, true, s.Summary, k.HasMask, k.Summary));
            Console.Error.Write('.');
        }

        Console.Error.WriteLine();
        var sheet = new JudgingSheets.SheetFile("mask-draw", id, DateTimeOffset.Now, Path.GetFullPath(photosDir), 0, null, null, Draw: items);
        File.WriteAllText(Path.Combine(outDir, "sheet.json"), JsonSerializer.Serialize(sheet, Json));
        var pageData = JsonSerializer.Serialize(new { id, items = items.Select(i => new { id = i.Id, photo = "img/" + i.Id + "-photo.jpg" }) });
        File.WriteAllText(Path.Combine(outDir, "index.html"), SheetPages.Fill(Page, pageData));

        report.AppendLine(Culture, $"Drawing sheet `{id}`: {items.Count} photos with people (seed {seed}); sky found by the rules on {items.Count(i => i.SkyFound)}.");
        report.AppendLine(Culture, $"Skipped {noPerson} photo(s) with no person, {unreadable} unreadable and {excluded} from earlier sheets.");
        report.AppendLine("The app's masks are in `app-masks/`; the page doesn't show them, so your outlines are drawn blind.");
        return 0;
    }

    public static int Score(string sheetDir, JudgingSheets.SheetFile sheet, string answersPath, StringBuilder report)
    {
        var answers = JsonSerializer.Deserialize<Dictionary<string, DrawnPhoto>>(File.ReadAllText(answersPath))
            ?? throw new InvalidDataException("The answers file is empty.");
        var subjectRows = new List<(DrawItem Item, double Iou, double EdgeF)>();
        var skyRows = new List<(DrawItem Item, double Iou, double EdgeF)>();
        int skyFalse = 0, skyMissed = 0, skyBothNone = 0, skipped = 0;
        var cutOnly = new List<string>();
        var inside = new Dictionary<string, double>();

        foreach (var item in sheet.Draw ?? [])
        {
            if (!answers.TryGetValue(item.Id, out var drawn))
            {
                continue;
            }

            if (drawn.Skipped || drawn.Subject is not { Count: > 0 })
            {
                skipped++;
                continue;
            }

            if (!drawn.Subject.Any(s => !string.Equals(s.Mode, "cut", StringComparison.OrdinalIgnoreCase)))
            {
                cutOnly.Add(item.Id);   // every shape was a Cut, so nothing was outlined; left out rather than guessed at
                continue;
            }

            int w = item.MaskWidth, h = item.MaskHeight;
            var mine = Rasterize(drawn.Subject, w, h);
            var app = ReadMask(Path.Combine(sheetDir, "app-masks", item.Id + "-subject.bin"), w, h);
            WriteComparison(Path.Combine(sheetDir, "img", item.Id + "-photo.jpg"), Path.Combine(sheetDir, "compare", item.Id + "-subject.jpg"), app, mine, w, h);
            subjectRows.Add((item, Iou(app, mine), EdgeF(app, mine, w, h, EdgeTolerance)));
            inside[item.Id] = Inside(app, mine);

            var mySky = drawn.NoSky || drawn.Sky is not { Count: > 0 } ? null : Rasterize(drawn.Sky, w, h);
            var appSky = item.SkyFound ? ReadMask(Path.Combine(sheetDir, "app-masks", item.Id + "-sky.bin"), w, h) : null;
            switch (mySky, appSky)
            {
                case (null, null): skyBothNone++; break;
                case (null, not null): skyFalse++; break;
                case (not null, null): skyMissed++; break;
                default: skyRows.Add((item, Iou(appSky!, mySky!), EdgeF(appSky!, mySky!, w, h, EdgeTolerance))); break;
            }
        }

        report.AppendLine(Culture, $"## Masks against your outlines — sheet `{sheet.Id}`");
        report.AppendLine();
        var cutNote = cutOnly.Count == 0 ? string.Empty : $"; {cutOnly.Count} left out because every shape on them was a Cut: {string.Join(", ", cutOnly)}";
        report.AppendLine(Culture, $"- Photos drawn: {subjectRows.Count} ({skipped} skipped{cutNote}).");
        report.AppendLine(Culture, $"- Overlap (IoU) is the shared area divided by the combined area: 1 is identical, 0.9 is close, 0.5 is half wrong. Edge agreement is the F-score of edge pixels within {EdgeTolerance} px of the other mask's edge (at the 1024 px analysis size).");
        Summary("Subject", subjectRows);
        if (inside.Count > 0)
        {
            var shares = inside.Values.Order().ToList();
            report.AppendLine(Culture, $"- **App mask inside your outline** (subject): median {shares[shares.Count / 2]:P0}, lowest {shares[0]:P0}. Near 100 % means the app added nothing you left out; loose outlines lower the IoU but not this.");
        }

        Summary("Sky (photos where both you and the rules found sky)", skyRows);
        report.AppendLine(Culture, $"- **Sky found or not:** both found sky on {skyRows.Count}; neither on {skyBothNone}; rules found sky you didn't draw on {skyFalse}; you drew sky the rules missed on {skyMissed}.");
        report.AppendLine();
        report.AppendLine("| Photo | File | App found | Subject IoU | App inside yours | Subject edge | Sky IoU |");
        report.AppendLine("|---|---|---|---:|---:|---:|---:|");
        foreach (var (item, iou, edge) in subjectRows)
        {
            var skyIou = skyRows.FirstOrDefault(r => r.Item.Id == item.Id) is { Item: not null } s ? s.Iou.ToString("0.00", Culture) : "–";
            report.AppendLine(Culture, $"| {item.Id} | {item.File} | {item.SubjectSummary} | {iou:0.00} | {inside[item.Id]:P0} | {edge:0.00} | {skyIou} |");
        }

        report.AppendLine();
        report.AppendLine("One person's outlines on a small set: this measures agreement with you, not general quality. The app's raw masks are compared, before the edge refinement the renderer adds.");
        return 0;

        void Summary(string name, List<(DrawItem Item, double Iou, double EdgeF)> rows)
        {
            if (rows.Count == 0)
            {
                report.AppendLine(Culture, $"- **{name}:** none.");
                return;
            }

            var ious = rows.Select(r => r.Iou).Order().ToList();
            var edges = rows.Select(r => r.EdgeF).Order().ToList();
            report.AppendLine(Culture, $"- **{name} ({rows.Count}):** IoU median {ious[ious.Count / 2]:0.00} (lowest {ious[0]:0.00}, highest {ious[^1]:0.00}); {rows.Count(r => r.Iou >= 0.9)} at 0.9 or more; edge agreement median {edges[edges.Count / 2]:0.00}.");
        }
    }

    /// <summary>The photo dimmed, with green where both masks agree, red for the app only and blue for your outline only.</summary>
    private static void WriteComparison(string photoPath, string outPath, bool[] app, bool[] mine, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var photo = new WicAnalysisImageLoader().Load(photoPath, Math.Max(w, h));
        var view = AnalysisImage.Create(photo.Width, photo.Height, (x, y) =>
        {
            var p = ((y * photo.Width) + x) * 4;
            var i = (Math.Min(h - 1, y * h / photo.Height) * w) + Math.Min(w - 1, x * w / photo.Width);
            byte r = (byte)(photo.Bgra[p + 2] / 3), g = (byte)(photo.Bgra[p + 1] / 3), b = (byte)(photo.Bgra[p] / 3);
            return (app[i], mine[i]) switch
            {
                (true, true) => (r, (byte)Math.Min(255, g + 140), b),
                (true, false) => ((byte)Math.Min(255, r + 170), g, b),
                (false, true) => (r, g, (byte)Math.Min(255, b + 190)),
                _ => (r, g, b),
            };
        });
        using var stream = File.Create(outPath);
        new WicJpegEncoder().Write(view, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 85);
    }

    // ------------------------------------------------------------------ pure measures (unit-tested)

    /// <summary>Fills the shapes in order at <paramref name="width"/> × <paramref name="height"/>: "add" sets, "cut" clears (even-odd inside each shape).</summary>
    internal static bool[] Rasterize(IReadOnlyList<DrawnShape> shapes, int width, int height)
    {
        var result = new bool[width * height];
        var crossings = new List<double>();
        foreach (var shape in shapes)
        {
            if (shape.Points.Length < 3)
            {
                continue;
            }

            var value = !string.Equals(shape.Mode, "cut", StringComparison.OrdinalIgnoreCase);
            for (var y = 0; y < height; y++)
            {
                var cy = (y + 0.5) / height;
                crossings.Clear();
                for (int i = 0, j = shape.Points.Length - 1; i < shape.Points.Length; j = i++)
                {
                    double x1 = shape.Points[j][0], y1 = shape.Points[j][1], x2 = shape.Points[i][0], y2 = shape.Points[i][1];
                    if ((y1 <= cy) != (y2 <= cy))
                    {
                        crossings.Add(x1 + ((cy - y1) / (y2 - y1) * (x2 - x1)));
                    }
                }

                crossings.Sort();
                for (var c = 0; c + 1 < crossings.Count; c += 2)
                {
                    var from = Math.Max(0, (int)Math.Ceiling((crossings[c] * width) - 0.5));
                    var to = Math.Min(width - 1, (int)Math.Floor((crossings[c + 1] * width) - 0.5));
                    for (var x = from; x <= to; x++)
                    {
                        result[(y * width) + x] = value;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Shared area over combined area; 1 when both are empty.</summary>
    internal static double Iou(bool[] a, bool[] b)
    {
        int both = 0, either = 0;
        for (var i = 0; i < a.Length; i++)
        {
            both += a[i] && b[i] ? 1 : 0;
            either += a[i] || b[i] ? 1 : 0;
        }

        return either == 0 ? 1 : both / (double)either;
    }

    /// <summary>Share of <paramref name="app"/> that lies inside <paramref name="mine"/>; 1 when the app mask is empty.</summary>
    internal static double Inside(bool[] app, bool[] mine)
    {
        int count = 0, both = 0;
        for (var i = 0; i < app.Length; i++)
        {
            if (app[i])
            {
                count++;
                both += mine[i] ? 1 : 0;
            }
        }

        return count == 0 ? 1 : both / (double)count;
    }

    /// <summary>F-score of edge pixels: an edge pixel of one mask counts when the other has an edge within <paramref name="tolerance"/> px.</summary>
    internal static double EdgeF(bool[] a, bool[] b, int width, int height, int tolerance)
    {
        var edgeA = Edges(a, width, height);
        var edgeB = Edges(b, width, height);
        var nearA = Dilate(edgeA, width, height, tolerance);
        var nearB = Dilate(edgeB, width, height, tolerance);
        int countA = 0, countB = 0, hitA = 0, hitB = 0;
        for (var i = 0; i < edgeA.Length; i++)
        {
            if (edgeA[i])
            {
                countA++;
                hitA += nearB[i] ? 1 : 0;
            }

            if (edgeB[i])
            {
                countB++;
                hitB += nearA[i] ? 1 : 0;
            }
        }

        if (countA == 0 && countB == 0)
        {
            return 1;
        }

        double precision = countA == 0 ? 0 : hitA / (double)countA, recall = countB == 0 ? 0 : hitB / (double)countB;
        return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    }

    private static bool[] Edges(bool[] mask, int width, int height)
    {
        var edges = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width) + x;
                if (mask[i] && ((x > 0 && !mask[i - 1]) || (x < width - 1 && !mask[i + 1]) || (y > 0 && !mask[i - width]) || (y < height - 1 && !mask[i + width])))
                {
                    edges[i] = true;
                }
            }
        }

        return edges;
    }

    private static bool[] Dilate(bool[] mask, int width, int height, int radius)
    {
        // Separable square dilation: rows, then columns.
        var rows = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            var last = int.MinValue / 2;
            for (var x = 0; x < width; x++)
            {
                last = mask[(y * width) + x] ? x : last;
                rows[(y * width) + x] = x - last <= radius;
            }

            last = int.MaxValue / 2;
            for (var x = width - 1; x >= 0; x--)
            {
                last = mask[(y * width) + x] ? x : last;
                rows[(y * width) + x] |= last - x <= radius;
            }
        }

        var result = new bool[mask.Length];
        for (var x = 0; x < width; x++)
        {
            var last = int.MinValue / 2;
            for (var y = 0; y < height; y++)
            {
                last = rows[(y * width) + x] ? y : last;
                result[(y * width) + x] = y - last <= radius;
            }

            last = int.MaxValue / 2;
            for (var y = height - 1; y >= 0; y--)
            {
                last = rows[(y * width) + x] ? y : last;
                result[(y * width) + x] |= last - y <= radius;
            }
        }

        return result;
    }

    private static void WriteMask(string path, MaskImage mask)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(mask.Width);
        writer.Write(mask.Height);
        writer.Write(mask.Alpha);
    }

    /// <summary>The app's mask, thresholded at half.</summary>
    private static bool[] ReadMask(string path, int width, int height)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadInt32() != width || reader.ReadInt32() != height)
        {
            throw new InvalidDataException("Mask size does not match the sheet: " + path);
        }

        return reader.ReadBytes(width * height).Select(a => a >= 128).ToArray();
    }

    private static bool IsJpeg(string f) => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static void Save(WicJpegEncoder encoder, AnalysisImage image, string path)
    {
        using var stream = File.Create(path);
        encoder.Write(image, FrameForge.Application.Editing.JpegMetadata.Nothing, stream, 90);
    }

    private const string Page = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Draw the masks</title>
        """ + SheetPages.Style + """
        <style>
        body{overflow:hidden}
        .help{margin:8px 18px 0;font-size:13px;text-align:center}
        .tools{display:flex;gap:8px;justify-content:center;align-items:center;padding:8px 12px;flex-wrap:wrap}
        .tools .sep{width:1px;height:22px;background:var(--border);margin:0 6px}
        #view{position:relative;overflow:hidden;margin:0 18px;height:calc(100vh - 200px);background:var(--sunken);border-radius:2px;cursor:crosshair}
        #inner{position:absolute;left:0;top:0;transform-origin:0 0}
        #inner img{display:block;user-select:none;-webkit-user-drag:none}
        #inner svg{position:absolute;left:0;top:0;overflow:visible}
        .layer-subject{--c:#F2A93B}.layer-sky{--c:#5B9BD5}
        button.on{border-color:var(--accent);color:var(--accent);font-weight:600}
        </style></head><body>
        <header><h1>Draw the masks</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted help">Outline the <b>subject</b> (the main people, with what they hold) and then the <b>sky</b>. Click to place points; click the first point or press <kbd>Enter</kbd> to close a shape.
        <b>Cut</b> shapes remove an area (the gap between an arm and the body). Wheel zooms, <kbd>Space</kbd>+drag pans, <kbd>Backspace</kbd> removes the last point or shape.</p>
        <div class="tools">
          <button id="lSubject">Subject <kbd>S</kbd></button><button id="lSky">Sky <kbd>K</kbd></button>
          <span class="sep"></span><button id="mAdd">Add <kbd>A</kbd></button><button id="mCut">Cut <kbd>C</kbd></button>
          <span class="sep"></span><button id="noSky">No sky in this photo <kbd>N</kbd></button><button id="fit">Fit <kbd>F</kbd></button>
          <span class="sep"></span><button id="prev">← Previous</button><button id="next">Next →</button><button id="skip">Skip photo</button>
          <span class="muted" id="status"></span>
        </div>
        <div id="view"><div id="inner"><img id="img" alt="Photo"><svg id="svg"></svg></div></div>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        let index = data.items.findIndex(i => !(answers[i.id] && answers[i.id].done)); if (index < 0) index = 0;
        let layer = "subject", mode = "add", current = [], zoom = 1, panX = 0, panY = 0, spaceDown = false, drag = null;
        const view = document.getElementById("view"), inner = document.getElementById("inner"), img = document.getElementById("img"), svg = document.getElementById("svg");
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const entry = () => answers[data.items[index].id] || (answers[data.items[index].id] = { subject: [], sky: [], noSky: false, skipped: false, done: false });
        function load() {
          current = []; img.onload = () => { fit(); draw(); }; img.src = data.items[index].photo; status();
        }
        function fit() {
          const vw = view.clientWidth, vh = view.clientHeight;
          zoom = Math.min(vw / img.naturalWidth, vh / img.naturalHeight); panX = (vw - img.naturalWidth * zoom) / 2; panY = (vh - img.naturalHeight * zoom) / 2; place();
        }
        function place() { inner.style.transform = "translate(" + panX + "px," + panY + "px) scale(" + zoom + ")"; draw(); }
        function toImage(e) { const r = view.getBoundingClientRect(); return [(e.clientX - r.left - panX) / zoom / img.naturalWidth, (e.clientY - r.top - panY) / zoom / img.naturalHeight]; }
        function draw() {
          const w = img.naturalWidth, h = img.naturalHeight, e = entry(), sw = 2 / zoom, rr = 4 / zoom;
          svg.setAttribute("width", w); svg.setAttribute("height", h); svg.setAttribute("viewBox", "0 0 " + w + " " + h);
          const poly = (pts, color, cut, closed) => {
            const p = pts.map(q => (q[0] * w) + "," + (q[1] * h)).join(" ");
            return closed ? '<polygon points="' + p + '" fill="' + (cut ? "rgba(0,0,0,.45)" : color.replace(")", ",.28)").replace("rgb", "rgba")) + '" stroke="' + color + '" stroke-width="' + sw + '"' + (cut ? ' stroke-dasharray="' + (6 / zoom) + '"' : "") + '/>'
                          : '<polyline points="' + p + '" fill="none" stroke="' + color + '" stroke-width="' + sw + '"/>';
          };
          let s = "";
          for (const [name, color] of [["sky", "rgb(91,155,213)"], ["subject", "rgb(242,169,59)"]]) for (const shape of e[name]) s += poly(shape.pts, color, shape.mode === "cut", true);
          if (current.length) {
            const color = layer === "sky" ? "rgb(91,155,213)" : "rgb(242,169,59)";
            s += poly(current, color, mode === "cut", false);
            s += current.map((q, i) => '<circle cx="' + q[0] * w + '" cy="' + q[1] * h + '" r="' + (i === 0 ? rr * 1.6 : rr) + '" fill="' + color + '"/>').join("");
          }
          svg.innerHTML = s;
          document.getElementById("progress").textContent = data.items.filter(i => answers[i.id] && answers[i.id].done).length + " of " + data.items.length + " done · photo " + (index + 1);
          for (const [id, on] of [["lSubject", layer === "subject"], ["lSky", layer === "sky"], ["mAdd", mode === "add"], ["mCut", mode === "cut"], ["noSky", e.noSky]]) document.getElementById(id).classList.toggle("on", on);
        }
        function status() { const e = entry(); document.getElementById("status").textContent = e.subject.length + " subject shape(s) · " + (e.noSky ? "no sky" : e.sky.length + " sky shape(s)"); }
        function close() {
          if (current.length >= 3) {
            const e = entry();
            // A Cut needs something to cut from: a layer's first shape is always Add.
            const m = mode === "cut" && !e[layer].some(s => s.mode === "add") ? "add" : mode;
            if (m !== mode) mode = "add";
            e[layer].push({ mode: m, pts: current }); if (layer === "sky") e.noSky = false; persist();
          }
          current = []; draw(); status();
        }
        function undo() {
          if (current.length) current.pop(); else { const e = entry(); e[layer].pop(); persist(); }
          draw(); status();
        }
        function go(step, skip) {
          if (current.length >= 3) close();
          const e = entry();
          if (skip) e.skipped = true;
          e.done = e.skipped || e.subject.length > 0;
          persist();
          index = Math.min(data.items.length - 1, Math.max(0, index + step)); layer = "subject"; mode = "add"; load();
        }
        view.addEventListener("mousedown", e => {
          if (spaceDown || e.button === 1) { drag = [e.clientX - panX, e.clientY - panY]; e.preventDefault(); return; }
          if (e.button !== 0) return;
          const p = toImage(e);
          if (current.length >= 3) {
            const f = current[0], dx = (p[0] - f[0]) * img.naturalWidth * zoom, dy = (p[1] - f[1]) * img.naturalHeight * zoom;
            if (dx * dx + dy * dy < 100) { close(); return; }
          }
          current.push([Math.min(1, Math.max(0, p[0])), Math.min(1, Math.max(0, p[1]))]); draw();
        });
        window.addEventListener("mousemove", e => { if (drag) { panX = e.clientX - drag[0]; panY = e.clientY - drag[1]; place(); } });
        window.addEventListener("mouseup", () => { drag = null; });
        view.addEventListener("wheel", e => {
          e.preventDefault(); const r = view.getBoundingClientRect(), mx = e.clientX - r.left, my = e.clientY - r.top, f = e.deltaY < 0 ? 1.25 : 0.8;
          panX = mx - (mx - panX) * f; panY = my - (my - panY) * f; zoom *= f; place();
        }, { passive: false });
        document.addEventListener("keydown", e => {
          const k = e.key.toLowerCase();
          if (e.key === " ") { spaceDown = true; view.style.cursor = "grab"; e.preventDefault(); return; }
          if (e.key === "Enter") close();
          else if (e.key === "Backspace") undo();
          else if (e.key === "Escape") { current = []; draw(); }
          else if (k === "s") { close(); layer = "subject"; draw(); }
          else if (k === "k") { close(); layer = "sky"; draw(); }
          else if (k === "a") { mode = "add"; draw(); }
          else if (k === "c") { mode = "cut"; draw(); }
          else if (k === "n") toggleNoSky();
          else if (k === "f") fit();
          else if (e.key === "ArrowRight") go(1);
          else if (e.key === "ArrowLeft") go(-1);
          else return;
          e.preventDefault();
        });
        document.addEventListener("keyup", e => { if (e.key === " ") { spaceDown = false; view.style.cursor = ""; } });
        function toggleNoSky() { const e = entry(); e.noSky = !e.noSky; if (e.noSky) e.sky = []; persist(); draw(); status(); }
        document.getElementById("lSubject").onclick = () => { close(); layer = "subject"; draw(); };
        document.getElementById("lSky").onclick = () => { close(); layer = "sky"; draw(); };
        document.getElementById("mAdd").onclick = () => { mode = "add"; draw(); };
        document.getElementById("mCut").onclick = () => { mode = "cut"; draw(); };
        document.getElementById("noSky").onclick = toggleNoSky;
        document.getElementById("fit").onclick = fit;
        document.getElementById("prev").onclick = () => go(-1);
        document.getElementById("next").onclick = () => go(1);
        document.getElementById("skip").onclick = () => go(1, true);
        document.getElementById("save").onclick = () => {
          if (current.length >= 3) close();
          const e = entry(); e.done = e.skipped || e.subject.length > 0; persist();
          const out = {}; for (const i of data.items) { const a = answers[i.id]; if (a && a.done) out[i.id] = { subject: a.subject, sky: a.sky, noSky: a.noSky, skipped: a.skipped }; }
          const link = document.createElement("a");
          link.href = URL.createObjectURL(new Blob([JSON.stringify(out)], { type: "application/json" }));
          link.download = "draw-answers-" + data.id + ".json"; link.click();
        };
        window.addEventListener("resize", fit);
        load();
        </script></body></html>
        """;
}
