# FrameForge AI

A Windows-first, local-first photography application focused on photo culling and, later, editing.

**New here? Read the [user guide](docs/user-guide.md)**: importing, culling, analysis, optional AI, Develop, masks,
presets and export, in plain steps.

**Status:** Milestone 08 (AI-assisted editing: Auto tools, and masks for local adjustments — Subject, Sky, Background, refined with clicks) is complete, after Milestone 06 (non-destructive editing). Milestone 07 is in progress: photos on an unplugged drive show as Offline, and **Locate folder…** reconnects a folder you moved; exports take file-name patterns, a metadata choice and saved profiles; RAW support has been tested on Canon EOS R50 samples but is not in the app yet. Import folders of JPEGs, cull them with the keyboard
(keep/reject/review, ratings, colour labels), filter and sort, compare photos side by side, and export the selection.
**Analyze** measures sharpness and exposure and finds exact and near-duplicates. These are explainable heuristics,
not AI. Optional local AI models (face detection and scene similarity, off by default, installed only with your consent)
add advisory badges and a suggested pick per group. They never change your decisions. Nothing is sent over the
internet: the only connections are model downloads you start, and the AI runtime's telemetry is switched off (see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [docs/licence-checklist.md](docs/licence-checklist.md)).
**Develop** edits exposure, white balance, contrast, highlights/shadows, saturation and vibrance, plus crop and
straighten, without ever changing the original file. Presets apply a look to many photos at once, and **Export** can
save edited JPEGs. **Auto** suggests a few options for light, levelling and cropping to faces; you pick one, or none.
The interface is a calm, neutral-grey workspace built around the photos; its rules are in
[docs/ui-design-system.md](docs/ui-design-system.md).

## Using it

The window has a **sidebar** (library views with counts: All photos, Undecided, Kept, To review, Rejected, Last import,
and Problems when there are any; then the folder tree; Settings and About at the bottom), a **header** with the current
view and the main actions (Import, Analyze, Compare, Presets, Export), a second row to refine by rating, label and
analysis finding and to choose the sort and thumbnail size, and a **footer** with progress and the library's totals.

1. **Import** (Ctrl+I) and choose a folder, or drop a folder onto the window. "Include subfolders" (the arrow beside
   Import) is remembered.
2. Import reads file metadata only; thumbnails are created as tiles scroll into view. Cancel at any time, and run
   the same import again to continue where it stopped. Re-importing never creates duplicates.
3. **Cull:** press **P** (keep), **X** (reject), **V** (review), **U** (unset), **1–5** (stars), **6–9** (labels).
   In the grid, keys apply to every selected photo (Shift/Ctrl+click, Shift+arrows). In the viewer (Enter), they apply
   to the current photo, and the next photo opens automatically after a Keep/Reject/Review/Unset (Settings → Culling).
   **Ctrl+Z / Ctrl+Y** undo and redo. Right-click a photo for the same actions. Press **F1** for all shortcuts.
4. **Work through what's left:** choose **Undecided** in the sidebar. Each decided photo leaves the view, and selection
   moves on. A folder in the sidebar shows that folder and its subfolders, combined with the chosen view.
5. **Compare:** select 2–4 photos and press **C**. ← / → choose the photo that decision keys apply to; each panel shows
   its AI pick, sharpest and blur findings.
6. **Export:** copy every Keep photo (or the current view) to a folder, or save a CSV manifest of the selection. Name
   the files with a pattern such as `{date}_{name}`, and save the settings as a profile (three are built in: Full-size
   JPEG, Web and Originals). The panel remembers your last export.
7. **Analyze:** measures every photo once and remembers the results; later runs only analyse new or changed photos.
   Tiles get marks: **≈ blur**, **◆** (the sharpest in its similar group) and **= dup** (identical file) always; **◐**
   (clipping) and **▣ n** (in a group of n similar photos) when you point at or select a tile. Use the **Analysis**
   filter to review them. Press **G** to
   select a photo's group (then **C** to compare), and **I** to see the exact numbers and why each badge was given.
   Thresholds are in **Settings → Analysis** and apply instantly. The defaults are starting points: tune them on your
   own photos. Analysis never changes decisions or deletes anything.
8. **Optional AI:**
   - In **Settings → AI models**, choose **Download…** to see the model's size, source, licence and checksum, then
     confirm. If you already have the file, use **Import file…** instead.
   - Turn on *faces* and/or *scenes*, save, and run **Analyze**.
   - Tiles get **AI pick** (the suggested keep in its group), **☺ soft** (all faces look soft) and, on hover, **☺ n**
     (faces found). An AI pick shows a Low/Medium/High confidence, and **I** shows the reason.
   - **Analysis → AI pick** and **Analysis → AI: faces soft** filter by them.

   AI runs only on this computer. Its quality has not yet been evaluated on a human-reviewed set, so treat it as a
   hint.
9. **Edit (Develop):** open a photo (Enter) and press **D**, or press **D** in the grid.
   - Move the sliders; the preview updates as you drag. Double-click a slider to reset it.
   - **Ctrl+Z / Ctrl+Y** undo and redo edits, hold **\\** to see the original, and **Reset all** goes back to it.
   - **Auto:** **Light**, **Level** and **Crop** each show up to three options below the photo, with a reason.
     ←/→ or 1–3 preview one on the photo, **Enter** applies it (one undo step), and **Esc** dismisses. Level draws the
     line it found; Crop frames the main faces at the ratio chosen under Crop, and needs face data from Analyze.
   - **R** opens crop: drag the frame or its corners, pick an aspect ratio, and use **Straighten** and ⟲ ⟳ to rotate.
   - Edits save automatically. Edited photos show **✎** in the grid, and their thumbnails show the edit.
   - **Presets:** in Develop, name the current settings and tick which groups to include. In the grid, **Presets**
     applies one to the selected photos (with cancel and **Undo last apply**). Its **Auto** section runs Light, Level
     and/or Crop over the selection, lists photos it couldn't help, and **Analysis → Changed by Auto** shows what it changed.
   - **Zoom to faces:** in the viewer, **F** / **Shift+F** step the 100% view through the detected faces (needs face
     data from Analyze).
   - **Export → Edited JPEGs** saves new JPEGs with the edits, at a quality and size you choose. Choose which camera
     metadata to keep (all, all except location, or none); your star rating and colour label are written in.

   Edits are stored separately and the original file is never written to. JPEGs are 8-bit, so highlights that are
   already blown cannot be recovered.

Originals are opened read-only and are never modified, moved, or deleted. Export never replaces an existing file: a
clashing name is saved as `name (2).jpg`. Unreadable files are listed as **Unreadable**, and files deleted since the
last import are listed as **Missing** the next time that folder is imported. Decisions are saved as you make them and
survive restarts; undo history does not.

## Requirements

- Windows 10 or 11
- .NET SDK 10.0.404 or a later 10.0 feature band (pinned in `global.json`)

## Build, test, run

From the repository root:

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/FrameForge.App
```

`dotnet test` uses Microsoft.Testing.Platform (opted in via `global.json`), which xunit.v3 requires on the .NET 10 SDK.

## Release

```powershell
./build/publish.ps1 -Version 0.2.0
```

1. Bump `<Version>` in `Directory.Build.props`, or pass `-Version`.
2. The script runs the tests, then builds a self-contained, ReadyToRun app for 64-bit Windows (with the .NET runtime's
   licence texts added).
3. It writes to `artifacts\`:
   - `FrameForge-<version>-win-x64-portable.zip`
   - `FrameForge-<version>-setup.exe`, when [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed

The installer is per user (no admin). Updating means running a newer installer over the old one; the library in
`%LOCALAPPDATA%\FrameForge` is kept, and the database is backed up before any upgrade. Crash reports stay on the
computer. See [ADR 0019](docs/decisions/0019-installer-updates-crash-reports.md).

## Where data is stored

| What | Location |
|---|---|
| Database (SQLite) | `%LOCALAPPDATA%\FrameForge\frameforge.db` |
| Logs (daily rolling) | `%LOCALAPPDATA%\FrameForge\logs\frameforge-YYYYMMDD.log` |
| Thumbnail cache | `%LOCALAPPDATA%\FrameForge\thumbnails\` |
| Optional AI models | `%LOCALAPPDATA%\FrameForge\models\<id>\<version>\` |

For development and testing, set the environment variable `FRAMEFORGE_DATA_DIR` to another folder before starting
the app. All of the above then go there instead, so test runs never touch your real library.

The database is created and migrated automatically on startup. The thumbnail cache can be deleted at any time; it is
rebuilt as needed. Deleting the whole `FrameForge` folder resets the app (your photos are not affected). The About
page also shows these paths.

## Keyboard

Press **F1** (or **?**) on the Library page for the same list in the app.

| Where | Shortcut | Action |
|---|---|---|
| Anywhere | Ctrl+1 / Ctrl+2 / Ctrl+3 | Library / Settings / About |
| Library | P / X / V / U | Keep / Reject / Review / Unset |
| Library | 1–5, 0 | Star rating, clear rating |
| Library | 6 / 7 / 8 / 9, Shift+0 | Red / yellow / green / blue label, clear label |
| Library | Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) | Undo / redo a decision |
| Library | C | Compare selected photos (2–4) |
| Library | G | Select the focused photo's similar group |
| Library | I | Show or hide analysis details |
| Library / viewer | D | Open or close Develop (edit) |
| Develop | R | Crop and straighten |
| Develop | Hold \ | Show the original |
| Develop (Auto options) | ← / → or 1–3, Enter, Esc | Preview an option, apply it, dismiss |
| Viewer | F / Shift+F | Zoom to the next / previous face |
| Develop | Ctrl+Z / Ctrl+Y | Undo / redo an edit |
| Library | F5 | Re-apply filter and sort |
| Library | Ctrl+I | Import folder |
| Grid | Ctrl+− / Ctrl+= | Smaller / larger thumbnails (three sizes) |
| Grid | Right-click, Menu key | Actions for the selected photos |
| Grid | ← / → | Previous / next photo (continues onto the next row) |
| Grid | ↑ / ↓ | Move by row |
| Grid | Shift+arrows, Shift/Ctrl+click | Select several |
| Grid | Enter / double-click | Open viewer |
| Viewer / compare | ← / → | Previous / next photo (viewer) or panel (compare) |
| Viewer | Home / End | First / last photo |
| Viewer | Z or double-click | Toggle fit ↔ 100% |
| Viewer | + / − | Zoom in / out |
| Viewer | Mouse wheel / drag | Zoom around the cursor / pan |
| Viewer / compare | Esc | Back to the grid |
| Settings | Ctrl+S | Save settings |

Decision keys apply to the selected photos in the grid, and to the current photo in the viewer and compare.

## Database migrations

EF Core tooling is pinned as a local tool (`dotnet-tools.json`):

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project src/FrameForge.Infrastructure --startup-project src/FrameForge.Infrastructure --output-dir Persistence/Migrations
```

A test (`Migrate_CreatesDatabase_AndModelMatchesMigrations`) fails if the EF model changes without a migration.

## Repository layout

```text
src/
  FrameForge.Core/            Domain types (settings, photos, decisions, library query); no dependencies
  FrameForge.Rendering/       Non-destructive edit rendering (recipe → pixels), shared by preview and export
  FrameForge.Analysis/        Deterministic image metrics and grouping on pixel buffers (no WPF, no AI)
  FrameForge.Application/     Use cases and ports (import, library, settings, thumbnail keys)
  FrameForge.Infrastructure/  SQLite/EF Core, file system scanning, thumbnail disk cache, logging, app data paths
  FrameForge.Imaging/         JPEG metadata, thumbnails, and display decoding via Windows Imaging Component
  FrameForge.AI/              Optional local models via ONNX Runtime (CPU): YuNet faces, DINOv2 scene embeddings,
                              YOLOX-s people + MobileSAM for subject masks
  FrameForge.App/             WPF composition root, shell, grid, viewer, view models, theme
tests/
  FrameForge.Analysis.Tests/  (synthetic image fixtures, no binary files)
  FrameForge.Application.Tests/
  FrameForge.Infrastructure.Tests/
  FrameForge.Imaging.Tests/   (Windows only; generates its JPEG fixtures at test time)
  FrameForge.App.Tests/       (Windows only)
  FrameForge.AI.Tests/        (real-model tests run only if FRAMEFORGE_TEST_MODELS points at the model files)
  FrameForge.Evaluation.Tests/
  FrameForge.Rendering.Tests/  (adjustment maths, geometry, golden pixels)
tools/
  FrameForge.Evaluation/      Model benchmark, labelled-set evaluation, face annotation, edit review sheets, Auto judging sheets (developer tool)
docs/                         Architecture, roadmap, milestones, design system, decisions
```

See [docs/architecture.md](docs/architecture.md) for boundaries and [docs/roadmap.md](docs/roadmap.md) for phases.

## Project documents

- `CLAUDE.md` — project instructions for Claude Code
- `docs/user-guide.md` — how to use FrameForge, for people using the app
- `docs/architecture.md` — architecture and boundaries (reflects the current implementation)
- `docs/decisions/` — architecture decision records
- `docs/roadmap.md` — phased roadmap and acceptance gates
- `docs/milestones/` — implementation briefs
- `docs/database-schema.md` — data model proposal
- `docs/ui-design-system.md` — UX principles and visual direction
- `docs/ai-models.md` — AI model strategy, selected models, how to run an evaluation
- `docs/visual-review-m06.md` — how edits were checked by eye, and how to repeat it
- `docs/visual-review-m08.md` — what was checked by eye for the Auto tools, and what wasn't
- `docs/evaluation-m08.md` — how to judge Auto light (blind A/B), Auto crop (your own crops) and masks (sky grading, hand-drawn outlines, speed) and score the results
- `THIRD-PARTY-NOTICES.md` — licences and attributions (also shown on the About page)
