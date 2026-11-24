namespace FrameForge.Evaluation.Sheets;

/// <summary>
/// The judging pages: plain HTML + JS opened from disk (no server, nothing leaves the computer). Answers are kept in
/// the browser while you work (so you can close and come back) and saved as a CSV for <c>score-sheet</c>.
/// </summary>
internal static class SheetPages
{
    /// <summary>Fills a page template with its data; "&lt;/" is escaped so the JSON cannot end the script early.</summary>
    public static string Fill(string template, string json) => template.Replace("__DATA__", json.Replace("</", "<\\/", StringComparison.Ordinal), StringComparison.Ordinal);

    internal const string Style = """
        <style>
        :root{--bg:#1C1E22;--sunken:#15171A;--raised:#25282D;--hover:#2E3238;--border:#363A41;--text:#E4E6E9;--muted:#9EA3AB;--accent:#5B9BD5;--error:#E0645C}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 "Segoe UI",system-ui,sans-serif}
        header{display:flex;align-items:center;gap:16px;padding:10px 18px;border-bottom:1px solid var(--border)}
        header h1{font-size:15px;font-weight:600;margin:0}
        header .grow{flex:1}
        .muted{color:var(--muted)}
        button{font:inherit;color:var(--text);background:var(--raised);border:1px solid var(--border);border-radius:4px;padding:6px 14px;cursor:pointer}
        button:hover{background:var(--hover)}
        button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
        button.chosen{border-color:var(--accent);color:var(--accent);font-weight:600}
        kbd{font:12px "Segoe UI",sans-serif;border:1px solid var(--border);border-bottom-width:2px;border-radius:3px;padding:0 5px;color:var(--muted)}
        .bar{display:flex;gap:8px;justify-content:center;align-items:center;padding:12px}
        .error{color:var(--error);min-height:1.4em;text-align:center;font-size:13px}
        .done{max-width:560px;margin:12vh auto;text-align:center}
        .done h2{font-weight:600}
        </style>
        """;

    public const string Light = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Auto light A/B</title>
        """ + Style + """
        <style>
        .pair{display:flex;gap:10px;justify-content:center;padding:10px 18px 0}
        figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:6px}
        figure img{max-width:calc(50vw - 30px);max-height:calc(100vh - 210px);display:block;border-radius:2px}
        figcaption{color:var(--muted);font-size:13px}
        </style></head><body>
        <header><h1>Auto light: blind A/B</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted" style="text-align:center;margin:10px 18px 0">Which version looks better as a finished photo? One is as shot, one is Auto light, in random order.</p>
        <main id="main"></main>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        let index = data.items.findIndex(i => !answers[i.id]); if (index < 0) index = data.items.length;
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const answered = () => data.items.filter(i => answers[i.id]).length;
        function render() {
          document.getElementById("progress").textContent = answered() + " of " + data.items.length + " answered";
          const main = document.getElementById("main");
          if (index >= data.items.length) {
            main.innerHTML = '<div class="done"><h2>All pairs answered</h2><p class="muted">Save your answers, then run <code>score-sheet</code> with the saved file. You can go back with <kbd>Backspace</kbd> to change any answer.</p><button id="save2">Save answers</button></div>';
            document.getElementById("save2").onclick = save; return;
          }
          const item = data.items[index], chosen = answers[item.id];
          main.innerHTML =
            '<div class="pair"><figure><img src="' + item.left + '" alt="Left version"><figcaption>Left</figcaption></figure>' +
            '<figure><img src="' + item.right + '" alt="Right version"><figcaption>Right</figcaption></figure></div>' +
            '<div class="bar"><button data-c="left">Left is better <kbd>←</kbd></button>' +
            '<button data-c="same">No real difference <kbd>↓</kbd></button>' +
            '<button data-c="right">Right is better <kbd>→</kbd></button>' +
            '<span class="muted" style="margin-left:16px">Pair ' + (index + 1) + ' · <kbd>Backspace</kbd> back</span></div>';
          main.querySelectorAll("[data-c]").forEach(b => { if (b.dataset.c === chosen) b.classList.add("chosen"); b.onclick = () => answer(b.dataset.c); });
          const next = data.items[index + 1]; if (next) { new Image().src = next.left; new Image().src = next.right; }
        }
        function answer(c) { answers[data.items[index].id] = c; persist(); index++; render(); }
        function save() {
          const lines = ["id,answer"].concat(data.items.filter(i => answers[i.id]).map(i => i.id + "," + answers[i.id]));
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([lines.join("\n") + "\n"], { type: "text/csv" }));
          a.download = "light-answers-" + data.id + ".csv"; a.click();
        }
        document.getElementById("save").onclick = save;
        document.addEventListener("keydown", e => {
          if (e.key === "ArrowLeft") answer("left");
          else if (e.key === "ArrowRight") answer("right");
          else if (e.key === "ArrowDown") answer("same");
          else if (e.key === "Backspace") { e.preventDefault(); index = Math.max(0, index - 1); render(); }
          else return;
          e.preventDefault();
        });
        render();
        </script></body></html>
        """;

    public const string Mask = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Subject masks</title>
        """ + Style + """
        <style>
        .pair{display:flex;gap:10px;justify-content:center;padding:10px 18px 0}
        figure{margin:0;display:flex;flex-direction:column;align-items:center;gap:8px}
        figure img{max-width:calc(50vw - 30px);max-height:calc(100vh - 250px);display:block;border-radius:2px;cursor:zoom-in}
        .seg{display:inline-flex;background:var(--sunken);border:1px solid var(--border);border-radius:5px;padding:2px;gap:2px}
        .seg button{border:0;background:transparent;color:var(--muted);padding:4px 12px;border-radius:3px}
        .seg button.on{background:var(--hover);color:var(--text);font-weight:600;box-shadow:inset 0 0 0 1px var(--accent)}
        .legend{text-align:center;margin:10px 18px 0;font-size:13px}
        </style></head><body>
        <header><h1>Subject masks</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted legend">Grade each mask: the subject should be lit, everything else dark, with the amber edge on the outline of the people who are the subject.
        <b>Good</b> = use as is · <b>Usable</b> = use after a small fix · <b>Bad</b> = wrong person, big parts missing, or much background.
        Hold <kbd>Space</kbd> to see the photo; click a picture to open it full size.</p>
        <main id="main"></main>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        const GRADES = [["good", "Good"], ["usable", "Usable"], ["bad", "Bad"]];
        const KEYS = { a: ["left", "good"], s: ["left", "usable"], d: ["left", "bad"], j: ["right", "good"], k: ["right", "usable"], l: ["right", "bad"] };
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        const complete = a => a && a.left && a.right;
        let index = data.items.findIndex(i => !complete(answers[i.id])); if (index < 0) index = data.items.length;
        let peek = false;
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const answered = () => data.items.filter(i => complete(answers[i.id])).length;
        function side(item, name, keys) {
          const a = answers[item.id] || {}, src = peek ? item.photo : item[name];
          return '<figure><a href="' + item[name] + '" target="_blank" rel="noopener"><img src="' + src + '" alt="' + (name === "left" ? "Left" : "Right") + ' mask"></a>' +
            '<span class="seg" role="group" aria-label="' + (name === "left" ? "Left" : "Right") + ' mask grade">' +
            GRADES.map(([g, label], n) => '<button data-side="' + name + '" data-g="' + g + '"' + (a[name] === g ? ' class="on"' : "") + '>' + label + ' <kbd>' + keys[n] + '</kbd></button>').join("") + '</span></figure>';
        }
        function render() {
          document.getElementById("progress").textContent = answered() + " of " + data.items.length + " graded";
          const main = document.getElementById("main");
          if (index >= data.items.length) {
            main.innerHTML = '<div class="done"><h2>All photos graded</h2><p class="muted">Save your answers, then run <code>score-sheet</code> with the saved file. <kbd>Backspace</kbd> goes back to change any grade.</p><button id="save2">Save answers</button></div>';
            document.getElementById("save2").onclick = save; return;
          }
          const item = data.items[index];
          main.innerHTML = '<div class="pair">' + side(item, "left", ["A", "S", "D"]) + side(item, "right", ["J", "K", "L"]) + '</div>' +
            '<div class="bar"><button id="back">Back <kbd>Backspace</kbd></button><span class="muted" style="margin-left:16px">Photo ' + (index + 1) + '</span></div>';
          main.querySelectorAll("[data-g]").forEach(b => b.onclick = () => grade(b.dataset.side, b.dataset.g));
          document.getElementById("back").onclick = back;
          const n = data.items[index + 1]; if (n) { new Image().src = n.left; new Image().src = n.right; }
        }
        function grade(sideName, g) {
          if (index >= data.items.length) return;
          const id = data.items[index].id; answers[id] = Object.assign({}, answers[id], { [sideName]: g }); persist();
          if (complete(answers[id])) { index++; }
          render();
        }
        function back() { index = Math.max(0, index - 1); render(); }
        function save() {
          const lines = ["id,left,right"].concat(data.items.filter(i => complete(answers[i.id])).map(i => i.id + "," + answers[i.id].left + "," + answers[i.id].right));
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([lines.join("\n") + "\n"], { type: "text/csv" }));
          a.download = "mask-answers-" + data.id + ".csv"; a.click();
        }
        document.getElementById("save").onclick = save;
        document.addEventListener("keydown", e => {
          const k = e.key.toLowerCase();
          if (KEYS[k]) grade(KEYS[k][0], KEYS[k][1]);
          else if (e.key === "Backspace") back();
          else if (e.key === " ") { if (!peek) { peek = true; render(); } }
          else return;
          e.preventDefault();
        });
        document.addEventListener("keyup", e => { if (e.key === " ") { peek = false; render(); } });
        render();
        </script></body></html>
        """;

    public const string MaskSingle = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Subject masks, round 2</title>
        """ + Style + """
        <style>
        .stage{display:flex;flex-direction:column;align-items:center;gap:10px;padding:10px 18px 0}
        .stage img{max-width:calc(100vw - 36px);max-height:calc(100vh - 250px);display:block;border-radius:2px;cursor:zoom-in}
        .legend{text-align:center;margin:10px 18px 0;font-size:13px}
        </style></head><body>
        <header><h1>Subject masks, round 2</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted legend">Grade the mask: the subject should be lit, everything else dark, with the amber edge on the outline of the people who are the subject.
        <b>Good</b> = use as is · <b>Usable</b> = use after a small fix · <b>Bad</b> = wrong person, big parts missing, or much background.
        Hold <kbd>Space</kbd> to see the photo; click the picture to open it full size.</p>
        <main id="main"></main>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        const GRADES = [["good", "Good", "1"], ["usable", "Usable", "2"], ["bad", "Bad", "3"]];
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        let index = data.items.findIndex(i => !answers[i.id]); if (index < 0) index = data.items.length;
        let peek = false;
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const answered = () => data.items.filter(i => answers[i.id]).length;
        function render() {
          document.getElementById("progress").textContent = answered() + " of " + data.items.length + " graded";
          const main = document.getElementById("main");
          if (index >= data.items.length) {
            main.innerHTML = '<div class="done"><h2>All photos graded</h2><p class="muted">Save your answers, then run <code>score-sheet</code> with the saved file. <kbd>Backspace</kbd> goes back to change any grade.</p><button id="save2">Save answers</button></div>';
            document.getElementById("save2").onclick = save; return;
          }
          const item = data.items[index], chosen = answers[item.id];
          main.innerHTML = '<div class="stage"><a href="' + item.mask + '" target="_blank" rel="noopener"><img src="' + (peek ? item.photo : item.mask) + '" alt="Mask"></a></div>' +
            '<div class="bar">' + GRADES.map(([g, label, k]) => '<button data-g="' + g + '"' + (chosen === g ? ' class="chosen"' : "") + '>' + label + ' <kbd>' + k + '</kbd></button>').join("") +
            '<button id="back">Back <kbd>Backspace</kbd></button><span class="muted" style="margin-left:16px">Photo ' + (index + 1) + '</span></div>';
          main.querySelectorAll("[data-g]").forEach(b => b.onclick = () => grade(b.dataset.g));
          document.getElementById("back").onclick = back;
          const n = data.items[index + 1]; if (n) { new Image().src = n.mask; }
        }
        function grade(g) { if (index >= data.items.length) return; answers[data.items[index].id] = g; persist(); index++; render(); }
        function back() { index = Math.max(0, index - 1); render(); }
        function save() {
          const lines = ["id,answer"].concat(data.items.filter(i => answers[i.id]).map(i => i.id + "," + answers[i.id]));
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([lines.join("\n") + "\n"], { type: "text/csv" }));
          a.download = "mask-answers-" + data.id + ".csv"; a.click();
        }
        document.getElementById("save").onclick = save;
        document.addEventListener("keydown", e => {
          if (e.key === "1") grade("good");
          else if (e.key === "2") grade("usable");
          else if (e.key === "3") grade("bad");
          else if (e.key === "Backspace") back();
          else if (e.key === " ") { if (!peek) { peek = true; render(); } }
          else return;
          e.preventDefault();
        });
        document.addEventListener("keyup", e => { if (e.key === " ") { peek = false; render(); } });
        render();
        </script></body></html>
        """;

    public const string Crop = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Crop comparison</title>
        """ + Style + """
        <style>
        .tools{display:flex;gap:6px;justify-content:center;align-items:center;padding:10px 18px 0;flex-wrap:wrap}
        .seg{display:inline-flex;background:var(--sunken);border:1px solid var(--border);border-radius:5px;padding:2px}
        .seg button{border:0;background:transparent;color:var(--muted);padding:3px 9px;border-radius:3px}
        .seg button.on{background:var(--hover);color:var(--text);font-weight:600}
        .stage{display:flex;justify-content:center;padding:10px 18px 0}
        .frame{position:relative;user-select:none;touch-action:none;cursor:crosshair;overflow:hidden;border-radius:2px}
        .frame img{display:block;max-width:calc(100vw - 36px);max-height:calc(100vh - 230px);pointer-events:none}
        .crop{position:absolute;border:1px solid #fff;box-shadow:0 0 0 9999px rgba(0,0,0,.55);cursor:move}
        .crop::before,.crop::after{content:"";position:absolute;inset:33.33% 0;border-top:1px solid rgba(255,255,255,.35);border-bottom:1px solid rgba(255,255,255,.35)}
        .crop::after{inset:0 33.33%;border:0;border-left:1px solid rgba(255,255,255,.35);border-right:1px solid rgba(255,255,255,.35)}
        .h{position:absolute;width:12px;height:12px;background:#fff;z-index:1}
        .h.nw{left:-6px;top:-6px;cursor:nwse-resize}.h.ne{right:-6px;top:-6px;cursor:nesw-resize}
        .h.sw{left:-6px;bottom:-6px;cursor:nesw-resize}.h.se{right:-6px;bottom:-6px;cursor:nwse-resize}
        .keep{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:#fff;background:rgba(0,0,0,.35);font-size:18px;pointer-events:none}
        </style></head><body>
        <header><h1>Crop comparison</h1><span class="muted" id="progress"></span><span class="grow"></span>
        <button id="save">Save answers</button></header>
        <p class="muted" style="text-align:center;margin:10px 18px 0">Crop each photo the way you would for sharing: drag to draw, drag inside to move, corners to resize. Faces are not marked on purpose.</p>
        <div class="tools"><span class="muted">Ratio</span><span class="seg" id="ratios"></span></div>
        <main id="main"></main>
        <script>
        const data = __DATA__;
        const key = "ff-sheet-" + data.id;
        const RATIOS = [["Free", null], ["Original", "orig"], ["1:1", 1], ["4:5", 0.8], ["2:3", 2 / 3], ["3:2", 1.5], ["16:9", 16 / 9]];
        let answers = {}; try { answers = JSON.parse(localStorage.getItem(key) || "{}"); } catch (e) { answers = {}; }
        let index = data.items.findIndex(i => !answers[i.id]); if (index < 0) index = data.items.length;
        let ratio = null, crop = null, drag = null;
        const persist = () => { try { localStorage.setItem(key, JSON.stringify(answers)); } catch (e) {} };
        const answered = () => data.items.filter(i => answers[i.id]).length;
        const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
        // Ratio in normalised units (crop w/h in 0-1 space) for the current photo.
        function nratio(item) { if (ratio === null) return null; const px = ratio === "orig" ? item.w / item.h : ratio; return px / (item.w / item.h); }
        function renderRatios() {
          const el = document.getElementById("ratios"); el.innerHTML = "";
          RATIOS.forEach(([label, value]) => { const b = document.createElement("button"); b.textContent = label; if (value === ratio) b.className = "on";
            b.onclick = () => { ratio = value; if (crop && index < data.items.length) fitRatio(data.items[index]); renderRatios(); draw(); }; el.appendChild(b); });
        }
        function fitRatio(item) {
          const r = nratio(item); if (r === null || !crop || crop === "none") return;
          const cx = crop.x + crop.w / 2, cy = crop.y + crop.h / 2; let w = crop.w, h = w / r;
          if (h > 1) { h = 1; w = h * r; } if (w > 1) { w = 1; h = w / r; }
          crop = { x: clamp(cx - w / 2, 0, 1 - w), y: clamp(cy - h / 2, 0, 1 - h), w, h };
        }
        function render() {
          document.getElementById("progress").textContent = answered() + " of " + data.items.length + " answered";
          const main = document.getElementById("main");
          if (index >= data.items.length) {
            main.innerHTML = '<div class="done"><h2>All photos answered</h2><p class="muted">Save your answers, then run <code>score-sheet</code> with the saved file. <kbd>Backspace</kbd> goes back to change any answer.</p><button id="save2">Save answers</button></div>';
            document.getElementById("save2").onclick = save; return;
          }
          const item = data.items[index], prev = answers[item.id];
          crop = prev === "none" ? "none" : prev ? { ...prev } : null;
          main.innerHTML = '<div class="stage"><div class="frame" id="frame"><img src="' + item.img + '" alt="Photo to crop"></div></div>' +
            '<div class="error" id="err"></div>' +
            '<div class="bar"><button id="back">Back <kbd>Backspace</kbd></button><button id="clear">Clear <kbd>C</kbd></button>' +
            '<button id="none">Keep the whole frame <kbd>N</kbd></button><button id="next">Next <kbd>Enter</kbd></button>' +
            '<span class="muted" style="margin-left:16px">Photo ' + (index + 1) + '</span></div>';
          const frame = document.getElementById("frame");
          frame.onpointerdown = down;
          document.getElementById("back").onclick = back;
          document.getElementById("clear").onclick = () => { crop = null; draw(); };
          document.getElementById("none").onclick = () => { crop = "none"; draw(); next(); };
          document.getElementById("next").onclick = next;
          frame.querySelector("img").onload = draw; draw();
          const n = data.items[index + 1]; if (n) new Image().src = n.img;
        }
        function draw() {
          const frame = document.getElementById("frame"); if (!frame) return;
          frame.querySelectorAll(".crop,.keep").forEach(e => e.remove());
          document.getElementById("err").textContent = "";
          if (crop === "none") { const k = document.createElement("div"); k.className = "keep"; k.textContent = "Whole frame kept"; frame.appendChild(k); return; }
          if (!crop) return;
          const c = document.createElement("div"); c.className = "crop";
          Object.assign(c.style, { left: crop.x * 100 + "%", top: crop.y * 100 + "%", width: crop.w * 100 + "%", height: crop.h * 100 + "%" });
          ["nw", "ne", "sw", "se"].forEach(k => { const h = document.createElement("div"); h.className = "h " + k; h.dataset.h = k; c.appendChild(h); });
          frame.appendChild(c);
        }
        function point(e) { const r = document.getElementById("frame").getBoundingClientRect(); return { x: clamp((e.clientX - r.left) / r.width, 0, 1), y: clamp((e.clientY - r.top) / r.height, 0, 1) }; }
        function down(e) {
          const p = point(e), handle = e.target.dataset && e.target.dataset.h;
          if (handle && crop && crop !== "none") drag = { mode: "resize", handle, start: { ...crop } };
          else if (e.target.classList.contains("crop")) drag = { mode: "move", p, start: { ...crop } };
          else drag = { mode: "new", anchor: p };
          e.currentTarget.setPointerCapture(e.pointerId);
          e.currentTarget.onpointermove = move; e.currentTarget.onpointerup = () => { drag = null; if (crop && crop !== "none" && (crop.w < 0.02 || crop.h < 0.02)) crop = null; draw(); };
        }
        function rectFrom(ax, ay, bx, by, item) {
          let w = Math.abs(bx - ax), h = Math.abs(by - ay); const r = nratio(item);
          if (r !== null) { if (w / Math.max(h, 1e-6) > r) h = w / r; else w = h * r; }
          const sx = bx >= ax ? 1 : -1, sy = by >= ay ? 1 : -1;
          w = Math.min(w, sx > 0 ? 1 - ax : ax); h = Math.min(h, sy > 0 ? 1 - ay : ay);
          if (r !== null) { if (w / Math.max(h, 1e-6) > r) w = h * r; else h = w / r; }
          return { x: sx > 0 ? ax : ax - w, y: sy > 0 ? ay : ay - h, w, h };
        }
        function move(e) {
          if (!drag) return; const p = point(e), item = data.items[index];
          if (drag.mode === "new") crop = rectFrom(drag.anchor.x, drag.anchor.y, p.x, p.y, item);
          else if (drag.mode === "move") { const s = drag.start; crop = { ...s, x: clamp(s.x + p.x - drag.p.x, 0, 1 - s.w), y: clamp(s.y + p.y - drag.p.y, 0, 1 - s.h) }; }
          else { const s = drag.start, h = drag.handle; const ax = h.includes("w") ? s.x + s.w : s.x, ay = h.includes("n") ? s.y + s.h : s.y; crop = rectFrom(ax, ay, p.x, p.y, item); }
          draw();
        }
        function next() {
          if (index >= data.items.length) return;
          if (!crop) { document.getElementById("err").textContent = "Draw a crop first, or choose Keep the whole frame."; return; }
          answers[data.items[index].id] = crop === "none" ? "none" : { x: +crop.x.toFixed(4), y: +crop.y.toFixed(4), w: +crop.w.toFixed(4), h: +crop.h.toFixed(4) };
          persist(); index++; render();
        }
        function back() { index = Math.max(0, index - 1); render(); }
        function save() {
          const lines = ["id,answer"].concat(data.items.filter(i => answers[i.id]).map(i => { const a = answers[i.id]; return i.id + "," + (a === "none" ? "none" : [a.x, a.y, a.w, a.h].join(",")); }));
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([lines.join("\n") + "\n"], { type: "text/csv" }));
          a.download = "crop-answers-" + data.id + ".csv"; a.click();
        }
        document.getElementById("save").onclick = save;
        document.addEventListener("keydown", e => {
          if (e.key === "Enter") next();
          else if (e.key === "Backspace") back();
          else if (e.key === "n" || e.key === "N") { if (index < data.items.length) { crop = "none"; next(); } }
          else if (e.key === "c" || e.key === "C") { crop = null; draw(); }
          else return;
          e.preventDefault();
        });
        renderRatios(); render();
        </script></body></html>
        """;
}
