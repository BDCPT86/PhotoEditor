# Architecture

## Goals
- Local-first desktop experience; no cloud dependency for core culling.
- Responsive interface over large photo collections.
- Original images remain untouched.
- AI capabilities are optional, replaceable, and separately testable.
- Domain/application logic can be tested without launching WPF.

## Solution structure

### Current (Milestone 06)

```text
FrameForge.sln
global.json                   SDK pin (10.0.404, latestFeature) + Microsoft.Testing.Platform runner
Directory.Build.props         net10.0 defaults: nullable, warnings-as-errors, latest-recommended analyzers
Directory.Packages.props      Central package versions
dotnet-tools.json             Local tools (dotnet-ef 10.0.15)
src/
  FrameForge.Core/            Settings; photo types (Photo, PhotoMetadata, ImportStatus, FolderScope, …);
                              decisions (PhotoDecision, CullState, ColourLabel, DecisionEdit);
                              library query (LibraryFilter, LibrarySort, LibraryQuery). No dependencies.
  FrameForge.Rendering/       Non-destructive edit rendering (net10.0, pure): EditRenderer (local adjustments from v2), EditGeometry, ToneOps, OKLab,
                              MaskImage, MaskField (guided-filter mask refinement)
  FrameForge.Analysis/        Deterministic metrics on pixel buffers (net10.0, depends on Core only): SharpnessAnalyzer,
                              ExposureAnalyzer, PerceptualHasher, SimilarityGrouper, AnalysisSettings. No WPF, no AI.
  FrameForge.Application/     Ports and use cases: settings, import, library, thumbnails (ThumbnailKey),
                              decisions (IDecisionService/DecisionService, DecisionHistory, IPhotoDecisionRepository),
                              export (IExportService, ExportNamePlanner, ManifestWriter),
                              analysis (PhotoAnalysisService, AnalysisCache, AnalysisInsights, thresholds, ports),
                              AI ports and use cases (AiModelCatalog, IModelStore, IAiRuntime, AiAnalysisService,
                              EmbeddingGrouper, AiInsights), editing (EditService, EditRenderService, EditHistory,
                              presets, RecipeJson, edited-export port), masks (IMaskService/MaskService, MaskKey,
                              SubjectMaskGenerator, SkyMaskGenerator, IMaskCache)
  FrameForge.Infrastructure/  EF Core/SQLite (DbContext, migrations, photo/decision/analysis/settings repositories),
                              FileSystemPhotoEnumerator, DiskThumbnailService, FileSystemExportService, Sha256FileHasher,
                              LogFileRetention, AppDataPaths, FileSystemModelStore (consent-gated, SHA-256 verified),
                              SqliteEdit/PresetRepository, FileSystemEditedExportService, DiskMaskCache
  FrameForge.Imaging/         WIC via WPF imaging (net10.0-windows): metadata, thumbnails, full/display/analysis decode
  FrameForge.AI/              ONNX Runtime (CPU): OnnxAiRuntime, YuNetFaceDetector + YuNetDecoder, DinoV2Embedder,
                              YoloxPersonDetector, MobileSamSegmenter, OnnxRun (cancellable runs), ImageOps
  FrameForge.App/             WPF: composition root, shell, Library (grid, filters, viewer, compare, export, help,
                              analysis details), Decision/AnalysisController, CullingShortcuts, PhotoGridListBox, …
tests/
  FrameForge.Analysis.Tests/        Synthetic fixtures: blur ordering, local sharpness, clipping, pHash robustness,
                                    Hamming distance, grouping (window, threshold, transitivity, order independence)
  FrameForge.Application.Tests/     Settings, import, decision rules, undo history, filter/sort determinism,
                                    export name planning, CSV manifest
  FrameForge.Infrastructure.Tests/  Real SQLite + file system: migrations, repositories (incl. decisions across restart),
                                    enumerator, thumbnail cache, import end-to-end, export safety
  FrameForge.Imaging.Tests/         JPEG fixtures generated in-test: EXIF parsing, orientation, thumbnails, corrupt files
  FrameForge.App.Tests/             Culling workflow through real view models (grid, viewer, compare, export),
                                    keyboard map, viewer, thumbnail loader, LRU cache, zoom math, AI badges/filters
  FrameForge.AI.Tests/              YuNet decoding/NMS on synthetic tensors, preprocessing, runtime failure handling;
                                    real-model tests run only when FRAMEFORGE_TEST_MODELS points at the model files
  FrameForge.Evaluation.Tests/      Evaluation metric maths, labels parsing, judging-sheet scoring
  FrameForge.Rendering.Tests/       Every adjustment's maths, geometry, recipe validation, preview≈export, golden pixels
tools/
  FrameForge.Evaluation/            Developer tool: model benchmark, labelled-set evaluation, Auto judging sheets (not shipped)
```

### Planned additions (introduce with their milestone)

```text
tests/FrameForge.Core.Tests/  when Core gains logic of its own
```

`FrameForge.Core.Tests` does not exist yet: Core's rules are small and are tested from the Application and App test projects. Preserve boundaries, not project count.

### Project dependencies

```text
App ──► Application ──► Rendering ──► Analysis ──► Core
 │  ──► Infrastructure ──► Application
 ├────► Imaging ───────► Application
 └────► AI ────────────► Application   (ONNX Runtime; implements IAiRuntime)
```

`Analysis` is a pure algorithm library that Application uses directly (like a maths library); the ports Application
defines for analysis (`IAnalysisImageLoader`, `IFileHasher`, `IAnalysisResultRepository`) cover the I/O.

Infrastructure and Imaging never reference each other: `DiskThumbnailService` (Infrastructure) depends on the
`IThumbnailGenerator` port, which Imaging implements. The App composes both.

## Implementation notes (Milestone 01: foundation)

### Composition root and startup
- `App.xaml.cs` is the composition root. `CompositionRoot.BuildHost` uses the .NET Generic Host (`Microsoft.Extensions.Hosting`) for DI, lifetimes, and `ILogger<T>`; Serilog is the only logging provider.
- Each layer exposes one registration extension: `AddFrameForgeApplication()`, `AddFrameForgeInfrastructure(paths)`, `AddFrameForgeImaging()`. View models, UI services, and `MainWindow` are registered in the App project.
- Startup order: create data folders → configure Serilog → build/start host → **show the shell** (status "Starting…", navigation disabled) → `AppStartup.RunAsync` (migrate DB, apply log retention, load start page) → navigation enabled.
- Fatal startup errors are logged and shown in a message box; the app then exits with code 1. Unhandled dispatcher exceptions are logged and reported to the user; unobserved task exceptions are logged.

### Threading
- `Microsoft.Data.Sqlite` performs I/O synchronously even behind async APIs. `SqliteSettingsStore` and `DatabaseMigrator` therefore wrap their work in `Task.Run` so callers on the UI thread are never blocked. Library code uses `ConfigureAwait(false)`; UI code resumes on the dispatcher.

### Persistence
- SQLite database at `%LOCALAPPDATA%\FrameForge\frameforge.db` (`AppDataPaths`).
- `IDbContextFactory<FrameForgeDbContext>`: one short-lived context per operation (desktop-friendly, thread-safe).
- Migrations live in `FrameForge.Infrastructure/Persistence/Migrations`. `DesignTimeDbContextFactory` lets `dotnet ef` run with Infrastructure as the startup project (the WPF app is not used for tooling).
- All `DateTime` properties are stored as UTC and re-marked `DateTimeKind.Utc` on read via a model-wide value converter.
- Schema: `AppSettings(Key TEXT PK max 200, ValueJson TEXT, UpdatedUtc TEXT)` — see `docs/database-schema.md`.

### Settings
- A setting is a `SettingDefinition<T>` (key, default, validation rule) declared in Core (`AppSettings`).
- `SettingsService` serialises values to JSON (enums by name). Reads return the default when nothing is stored; when the stored value is unreadable or fails validation, the default is returned **and a warning is logged**. Writes reject invalid values with `ArgumentOutOfRangeException`.
- Current settings: `app.startPage` (Library/Settings/About, default Library) and `logging.retentionDays` (1–365, default 14), both applied on next launch; `import.includeSubfolders` (default true) and `import.lastFolder`, remembered from the Library page; `viewer.autoAdvance` (default true), on the Settings page; `analysis.maxPixelSize` (512/1024/2048, default 1024) and the analysis thresholds `analysis.blurThreshold` (60), `analysis.highlightClipPercent` (1), `analysis.shadowClipPercent` (5), `analysis.maxHashDistance` (10), `analysis.burstWindowSeconds` (10, 0 = no limit).

### Logging
- Serilog file sink, daily rolling, 50 MB size roll, `%LOCALAPPDATA%\FrameForge\logs\frameforge-YYYYMMDD.log`. `Microsoft.*` categories are filtered to Warning (EF SQL is not logged).
- Age-based cleanup is performed by `LogFileRetention` at startup using the user's retention setting (Serilog's count-based retention is disabled). It only touches `frameforge-*.log` in the logs folder.

### Navigation and MVVM
- CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`).
- `ShellViewModel` owns `Pages` (`NavigationItem`: page enum, shortcut, page view model), `SelectedPage`, and `CurrentPage`. Page view models are injected — no service locator.
- Views are resolved by implicit `DataTemplate`s (view model type → view) in `MainWindow.xaml`. Code-behind contains only `InitializeComponent` and (for `MainWindow`) DataContext assignment from DI.
- `PageViewModel.OnNavigatedToAsync` lets a page load data when shown (Settings reloads stored values).
- Keyboard: Ctrl+1/2/3 navigate, arrow keys move in the navigation list, Ctrl+S saves settings, visible focus styling throughout.

### UI theme
- `Themes/Theme.xaml` holds colour tokens, brushes, typography, and control styles per `docs/ui-design-system.md` (charcoal surfaces, single accent, status colours only for meaning; selection shown by indicator bar + weight, not colour alone; status messages include a glyph).
- Per-monitor V2 DPI awareness is declared in `app.manifest`.

## Implementation notes (Milestone 02: import and viewer)

### Import pipeline (`PhotoImportService`)
1. **Scan** (`FileSystemPhotoEnumerator`, on the thread pool): walks folders explicitly, so an unreadable
   sub-folder is reported in `ImportSummary.SkippedFolders` and logged, never silently skipped.
   Junctions and system folders are not followed. Only `.jpg`/`.jpeg` (any case).
2. **Diff** against stored `PhotoFileState`s (path, size, last-write UTC, status). A file is skipped when the path
   matches case-insensitively, size and timestamp are equal, and the last status was `Imported`.
   Failed and Missing records are always retried.
3. **Read** changed/new files in batches of 200 with `Parallel.For` (max parallelism `min(cores/2, 4)`).
   `PhotoReadException`, `IOException`, and `UnauthorizedAccessException` mark only that file as `Failed`
   with `LastError`; any other exception is treated as a bug and aborts the import (after saving the batch).
4. **Save** each batch in one transaction. Writes are not cancellable: when cancelled, the partially read batch is
   still saved, so re-running the import resumes without re-reading anything already stored.
5. **Mark missing** (only after a complete, uncancelled scan): records in the imported scope whose files were not
   found become `Missing`. Records under skipped folders and outside the import scope are left untouched.
   Records are never deleted by import.
- Progress is throttled to one report per 100 ms; the summary is returned (not thrown) on cancellation.

### Data model
- `Photos` table (migration `AddPhotos`): `FilePath` and `FolderPath` use SQLite `NOCASE` collation, and `FilePath`
  has a unique index, so the database itself rejects case-variant duplicates.
- `Status` is stored as a string. `CaptureLocalTime` is the camera's EXIF wall-clock time (not UTC: EXIF has no
  reliable time zone), so it is excluded from the UTC converter. Width/height are stored pixel dimensions, before
  EXIF orientation.
- No `Thumbnail` table: the cache key is computed, so the database is not needed to find a thumbnail.

### Imaging (WIC through WPF, `FrameForge.Imaging`)
- **Metadata:** header and metadata only (`BitmapCreateOptions.DelayCreation`, no pixel decode). Raw EXIF tag
  queries (e.g. `/app1/ifd/exif/{ushort=36867}`) rather than Windows property-system names. Each tag read is
  independent; a malformed tag yields null for that field only.
- **Decoding:** source files are opened `FileAccess.Read` with permissive sharing, never written. Bitmaps are decoded
  with `CacheOption.OnLoad` and frozen, so they can be created on worker threads and bound on the UI thread.
  EXIF orientation is applied with an orthogonal `TransformedBitmap`, then materialised once with `CachedBitmap`.
- WIC errors (`NotSupportedException`, `FileFormatException`, COM errors, …) become `PhotoReadException`;
  `FileNotFoundException`/`IOException` pass through so callers can distinguish *missing* from *corrupt*.

### Thumbnails
- **Disk cache** (`DiskThumbnailService`): `%LOCALAPPDATA%\FrameForge\thumbnails\<2-char shard>\<key>.jpg`, longest side
  320 px, JPEG quality 85, orientation applied. `ThumbnailKey` = SHA-256 of upper-cased path, size, mtime ticks,
  max size, generator version. Written to a temp file and moved into place. Missing files are regenerated on demand;
  the folder can be deleted at any time.
- **Generated on demand**, not during import: import stays fast (≈2,000 files/3 s, ≈1,000 files/s for small files on
  the development machine) and only thumbnails that are actually viewed cost anything.
- **Memory** (`ThumbnailLoader`, App): an LRU of 400 decoded thumbnails, used only from the UI thread. Tiles do not
  hold bitmaps; `PhotoTileViewModel.Thumbnail` asks the cache on each binding read and requests a load on a miss.
- **Scheduling:** 3 workers, pending requests served newest-first (what the user just scrolled to), capped at 256
  pending, with the oldest dropped. A dropped tile requests again the next time it is realised.

### Grid and viewer
- `PhotoGridListBox` (a `ListBox`) + `VirtualizingWrapPanel` (Recycling, pixel scrolling, one page of cache): with
  22,000 photos only about 50 containers are realised. Enter or double-click opens the viewer. See Milestone 03 for
  arrow-key and selection behaviour.
- A full rebuild replaces `Photos` in one assignment (one notification rather than thousands).
- `ViewerViewModel` decodes **only the current photo** at full resolution behind a one-at-a-time gate; navigation
  cancels the previous load, and the thumbnail is shown as a placeholder while decoding. Closing releases the bitmap.
  Navigation stops at the first/last photo (no wrap-around).
- `ZoomPanImage` (presentation-only control): fit (never upscaled past 100%), 100% = one image pixel per *device*
  pixel (DPI-aware), wheel zoom around the cursor, drag pan clamped to keep the image in view, Z / + / − keys
  (digits are reserved for ratings and labels since Milestone 03).
  The image is hosted in a `Canvas` so WPF layout never clips it before the zoom transform. Pure math is in `ZoomMath`.
- Problem states are shown with a glyph and text (`Missing`, `Unreadable`, `File not found`, `Can't display`) and
  exposed to screen readers through `AutomationProperties.ItemStatus`.

## Implementation notes (Milestone 03: culling)

### Selection, focus, and decision are separate
- **Selection:** the highlighted grid tiles (extended: Shift/Ctrl+click, Shift+arrow keys). Passed to the view model
  through `ListBoxBehaviors.SelectedItemsSink`, because WPF cannot bind `SelectedItems`.
- **Focus:** the photo the keyboard is on: the focused tile, the viewer's current photo, or compare's active panel.
- **Decision:** the persisted `PhotoDecision` (state, rating, label). Decision keys apply to the **selection** in the
  grid, and to the **current photo** in the viewer and compare (`LibraryViewModel.DecisionTargets`).

### Decisions
- `PhotoDecision` is immutable and always valid (rating 1–5 or null; defined enums). `DecisionEdit` describes one kind
  of change; `DecisionService.Plan` turns an edit plus targets into a `DecisionChange` (before/after per photo,
  leaving out photos that would not change).
- `DecisionController` (App, UI thread) applies a change to the tiles **immediately**, records it in `DecisionHistory`
  (undo/redo, 200 entries, multi-photo changes undo as one step), then saves. If the save fails, the tiles are reverted
  and the change is removed from history, so the screen, the database and undo always agree.
- `DecisionService` serialises saves with a semaphore, so a later decision can never be overwritten by an earlier one.
  `LoadAsync` waits for in-flight saves, so a reload never shows older state than the screen.
- Storage: `PhotoDecisions` table (migration `AddPhotoDecisions`), one row per photo with a non-empty decision;
  saving an empty decision deletes the row. A check constraint enforces rating 1–5; the foreign key to `Photos` cascades.

### Keyboard
- `CullingShortcuts` is the single key table. It drives both handling and the help panel. WPF `KeyBinding` cannot
  express plain letters and digits, so `ShortcutBehavior` (attached to the Library page) resolves the bubbling `KeyDown`,
  after focused controls (zoom, list navigation) have had their turn, and ignores keys typed into text boxes.
- `PhotoGridListBox`: WPF's spatial navigation in a wrap layout treats ← / → as "within the row" (it stopped or cycled at
  row ends). The grid instead moves ← / → through photos in order and extends Shift ranges from its own anchor (last
  photo reached without Shift); ↑ / ↓, Home/End and mouse selection use the base list box, whose anchor is kept in sync.
- Focus returns to the selected tile whenever the viewer or compare closes; otherwise page shortcuts would stop.

### Filtering and sorting
- `LibraryQuery.Apply` is a pure function over the in-memory list. Every sort ends with file path (ordinal,
  case-insensitive) and then photo id, so equal keys never give an arbitrary order (tested with shuffled input).
- When a decision changes in the grid, photos that **no longer match** are removed in place (scroll position kept,
  selection moves to the next photo), so filtering on Unset works as a queue. If a change makes a hidden photo match
  (e.g. undo), the view is rebuilt. Sort order is **not** re-applied after a rating change until F5, a filter or sort
  change, or closing the viewer/compare, so photos do not jump under the cursor.
- The viewer and compare work on a **snapshot** taken when they open, so navigation stays stable while deciding; the
  grid is rebuilt when they close.

### Compare
- 2–4 selected photos, in grid order. Images are decoded at display size (longest side 2,048 px via
  `LoadForDisplay`), at most two decodes at a time, and released on close.

### Export (`FileSystemExportService`)
- Scope: all Keep photos (ignoring the filter) or the current grid view. Modes: copy files, or a CSV manifest.
- **Sources are only read** (read-only, sharing-friendly streams).
- **Nothing is overwritten:** `ExportNamePlanner` picks names that do not collide, case-insensitively, with existing
  entries or with each other (`name (2).ext`). Each file is copied to a hidden temporary file created with
  `FileMode.CreateNew`, then moved with `overwrite: false`. If the name was taken in the meantime, the next free name
  is used.
- Modified time is preserved. Missing or unreadable sources are listed and the rest continue. Cancelling deletes the
  in-progress temporary file, so only complete files remain.
- Manifest: RFC 4180 CSV, UTF-8 with BOM. Values starting with `= + - @` are prefixed with `'` to prevent spreadsheet
  formula injection. It refuses a path equal to any exported photo, and is written via temp file + move (the save
  dialog has already confirmed replacing an existing manifest).
- Exporting into (or above) an imported folder asks for confirmation, because the copies would be imported next time.

## Implementation notes (Milestone 04: deterministic analysis)

Everything here is a heuristic measurement, labelled as such in the UI, never called AI. Nothing is changed,
decided, or deleted automatically.

### Metrics (`FrameForge.Analysis`)
| Kind | Method | Version string |
|---|---|---|
| Sharpness | Variance of the 4-neighbour Laplacian of luma, per cell of an N×N grid (default 4). Score = sharpest cell, so a sharp subject on a soft background scores sharp. | `laplacian-variance-grid-v1` |
| Exposure | Mean luma; % pixels with any channel ≥ 254 (highlights); % with all channels ≤ 1 (shadows); 16-bin histogram. Measured on the JPEG as saved. | `luma-mean-channel-clip-v1` |
| Perceptual hash | Luma area-averaged to 32×32 → 2-D DCT-II → 8×8 low frequencies → bit = above median (DC excluded from median). | `dct-phash-32x8-v1` |
| Content hash | SHA-256 of the file bytes (streamed, read-only). | `sha256-v1` |
| Grouping | Hamming distance ≤ limit, optionally within a burst window; union-find; deterministic order. | `phash-hamming-unionfind-v1` |

### Pipeline (`PhotoAnalysisService`)
- For each imported photo, it checks which kinds are missing or stale, decodes **once** at the analysis resolution
  (`WicAnalysisImageLoader`: JPEG-domain downscale during decode, EXIF orientation, BGRA32), computes the needed
  metrics, and hashes the file if needed.
- `Parallel.For` with `MaxParallelism = min(cores/2, 4)`, in batches of 50 written in one transaction. Cancellation
  saves the batch in progress; the next run resumes. Unreadable files are counted and logged, and the rest continue.
- Results are stored in `AnalysisResults` with the algorithm version, a per-kind settings hash, and the source's size
  and modified time. `AnalysisCache.IsValid` requires all four to match.

### Benchmark (development machine, 20 logical cores, 4 workers)
Synthetic burst set: 500 scenes × 4 frames 1 s apart (the 4th softened), plus 3 byte copies and 2 corrupt files.

| Run | Photos | Time | Per photo |
|---|---|---|---|
| 2400×1600 JPEGs, analysed at 1,024 px | 2,003 | 23.5 s | 11.7 ms |
| 1200×800 JPEGs, analysed at 1,024 px | 2,003 | 16.9 s | 8.4 ms |
| Same set, second run (all cached) | 2,003 | 0.1 s | — |
| Same set, re-analysed at 512 px | 2,003 | 6.0 s | 3.0 ms |

- **UI responsiveness:** the window's message loop answered `WM_NULL` in about 1 ms when idle and 0.6 ms on average
  (1.1 ms max) during analysis. An earlier figure of about 400 ms came from a test helper that searched the whole UI
  Automation tree for an element name that no longer exists; it measured the tree walk, not the app.
- **Grouping accuracy:** at 1200×800, every burst formed a group (500 groups) and no bursts were wrongly merged. In 3
  bursts one frame exceeded the 10-bit default (12–20 bits; mostly the softened frame) and was left out of its group.
  The earlier 2400×1600 run's 499 groups was the same tolerance limit, not a defect.
- Synthetic fixtures only. Real photos may differ in speed and in how well the default thresholds fit.

### Testing against an isolated data folder
Set `FRAMEFORGE_DATA_DIR` to a folder before starting the app (development/testing only) to keep its database, logs
and thumbnail cache there instead of `%LOCALAPPDATA%\FrameForge`. The app logs a warning when the override is
active. Use this for any test run, so a real library is never touched.

### Settings vs thresholds
- **Computation** (`AnalysisSettings`: resolution, grid) is in the settings hash. Changing the resolution makes
  image metrics stale but **not** the content hash (it has no settings).
- **Interpretation** (`AnalysisThresholds`: blur score, clipping limits, hash distance, burst window) is applied by
  `AnalysisInsights.Build` in memory. Changing a threshold re-flags and regroups instantly, with no re-analysis
  (tested).

### Explainability
- `AnalysisInsights` produces, for each photo, the flags plus `InsightLine`s: value, threshold, method, and caveats.
  For example: the sharpest cell and the whole-image figure; clipping percentages against their limits; the nearest
  similar photo, the bit distance and the time gap; exact-duplicate peers.
- Within each group the sharpest photo is marked "sharpest in group", as a **suggestion** only.
- UI: tile badges (glyph + word), an ANALYSIS filter row (Blurry?, Clipping, Similar groups, which shows groups
  together, and Duplicates), **G** selects the focused photo's group (then **C** to compare it), and **I** opens the
  details panel for the current photo (grid, viewer or compare). **G** changes the selection but deliberately leaves
  keyboard focus on the photo you were on (moving focus during the key press would make the list collapse the
  selection, and selection and focus are meant to be separate).

## Implementation notes (Milestone 05: optional AI)

AI is **optional and advisory**. It is off by default, the app is fully usable with no model installed, and nothing
AI produces is ever written to `PhotoDecisions` (tested). Model choices, licences and checksums are in
[ADR 0005](decisions/0005-local-ai-models.md) and [ai-models.md](ai-models.md).

### Models and acquisition
- `AiModelCatalog` (Application) lists each model with its pinned download URL (an immutable commit or revision),
  size, SHA-256, licence, copyright and source page. `VersionTag` = `id@version#sha12` is stored with every result.
- `FileSystemModelStore` (Infrastructure) keeps files in `%LOCALAPPDATA%\FrameForge\models\<id>\<version>\`.
  - **Download** requires a `ModelDownloadConsent` for that exact model. The UI creates it only from the consent
    panel's "I agree, download" button, after showing the size, URL, SHA-256 and licence. The download is written to
    a `.partial` file, checked for size and SHA-256, and then moved into place. A mismatch is deleted and reported.
  - **Import** works the same way for offline installs (the user's file is copied, never moved).
  - **Remove** deletes the model folder.
  - Status checks the file size on every load (cheap); the full hash is verified at install.
- `OnnxAiRuntime` (FrameForge.AI) loads models lazily on the CPU execution provider (2 intra-op threads, 1 inter-op).
  A missing model gives `null`. A model that fails to load gives `null` plus `LastError`, is logged, and is not
  retried until `Reset()` (called after install, remove or a capability change).

### Inference
| Model | Pre-processing | Output used |
|---|---|---|
| YuNet (2026may, dynamic input) | Longest side 1024 (640 missed most distant faces in a real sports set), BGR 0–255, zero-pad right/bottom to a multiple of 32 | `cls/obj/bbox/kps` at strides 8/16/32. Decoded as OpenCV's FaceDetectorYN does: score = √(cls·obj), then greedy NMS with IoU 0.3. |
| DINOv2-small | Shortest edge 256 (bilinear; the reference uses bicubic), centre crop 224, RGB, ImageNet mean/std | CLS token of `last_hidden_state` (384 values), L2-normalised |

- Each face's sharpness is the M04 Laplacian-variance measure on the face crop. It is not measured (null) for faces
  under 16 px at the analysis resolution, because such crops are mostly noise. The detector's `VersionTag` includes
  its input size (`…@in1024`), so changing it re-runs face detection.
- `AiAnalysisService` runs after the deterministic pass in the same Analyze command. It decodes each photo
  again at the analysis resolution (the deterministic pass's image is not shared), runs `Parallel.For` with 2 photos at a time, and batches of 25 saved even on cancellation. A photo that fails
  inference is counted and logged, and the rest continue.
- **Cache:** the `AnalysisResults` rows have kinds `Faces` (10) and `SceneEmbedding` (11). Validity requires the
  model `VersionTag`, the settings hash (resolution, and the stored face-score floor of 0.5), and the source's size and
  modified time to match. A different model file invalidates results automatically (tested). Embeddings are stored as
  base64 float32.

### Interpretation (`AiInsights`, pure)
- Faces are counted at the current score threshold (default 0.6); the badge shows all counted faces.
- **Main faces** are counted faces that are measurable and at least half the width of the largest one. Only main
  faces decide "faces soft" (**all** main faces below the soft threshold, default 40) and the pick, so a blurred
  spectator in the background never makes a sharp subject look soft.
- **Scene groups:** cosine similarity ≥ 0.9 within the shared burst window, using union-find (`EmbeddingGrouper`,
  deterministic).
- **Suggested pick:** one per group (the scene group, otherwise the M04 similar-photo group).
  - If at least two members have main faces, the pick is the photo whose softest main face is sharpest. Otherwise it is the
    photo with the highest whole-photo sharpness. A single face may be a false detection; one was seen on a synthetic
    texture during testing.
  - Confidence comes from the margin to the runner-up: ≥ 30 % High, ≥ 10 % Medium, otherwise Low.
- Every AI line in the details panel says *"advisory only, not yet evaluated on a human-reviewed set"*.
- Threshold changes re-interpret stored results instantly. Turning a capability on or off reloads from the cache.

### UI
- Tile badges: **☺ n** (faces), **AI faces soft**, **AI pick**, all with tooltips. Filter chips: **AI pick** and
  **AI faces soft**. AI lines are added to the **I** details panel.
- The status bar after Analyze reports the AI counts, or why AI was skipped (model not installed or failed to load).
- Settings → *AI models*: status, version, size, licence, source, SHA-256, **Download…** (opens the consent panel),
  **Import file…**, **Remove**, with download progress and cancel. Below that are the two capability toggles (off by
  default) and three thresholds.

### Benchmark and evaluation
- `tools/FrameForge.Evaluation benchmark` measured median CPU time per photo on synthetic 1024×683 images:
  YuNet 36.1 ms (at 1024 px input), DINOv2-small 87.7 ms. Peak working set was 357 MB. See [m05-ai-benchmark.md](benchmarks/m05-ai-benchmark.md).
- `… evaluate --photos <dir> --labels <csv>` reports scene-grouping pairwise precision and recall, face recall by
  count, pick agreement, false-rejection rate, latency per 1,000 photos and peak memory.
- **No human-reviewed evaluation set exists yet, so no quality claims are made.**

## Implementation notes (Milestone 06: non-destructive editing)

Originals are never written to. An edit is a recipe stored in the database and applied when a photo is shown or
exported. Design and alternatives are in [ADR 0006](decisions/0006-non-destructive-editing.md); the visual check is
in [visual-review-m06.md](visual-review-m06.md).

### Recipe and rendering
- `EditRecipe` (Core) holds:
  - exposure (EV), temperature, tint, contrast, highlights, shadows, saturation, vibrance
  - quarter turns, straighten (±45°), and a crop normalised to the straightened frame's inscribed rectangle

  It is neutral when every field is 0 and is validated at every boundary.
- `EditRenderer.Render(source, recipe, maxLongSide)` works in this order:
  1. geometry (inverse mapping + bilinear sampling)
  2. linear light
  3. white-balance/exposure gains
  4. tone curve on luminance (applied as a gain, so hue is kept)
  5. OKLab chroma
  6. sRGB

  The source is never modified, and a neutral recipe returns identical bytes.
- **Same code everywhere:** the viewer (full size), Develop preview (2048 px), edited thumbnails (from the cached
  thumbnail) and export (full size, or the chosen long side) all use it.
- **Decode size:** `EditGeometry.DecodeLongSide` decodes at the smallest size the output needs, so WIC does the
  downscaling, which filters better than bilinear sampling.

### Storage and services
- `PhotoEdits` (one row per edited photo; neutral = no row; cascade delete) and `EditPresets` (unique name, ignoring
  case) are added by migration `AddPhotoEditsAndPresets`. Recipes are stored as validated JSON; unreadable rows are
  logged and skipped.
- `EditService`: load, save, and batch preset apply (batches of 100, progress, cancel keeps finished batches,
  before/after recorded).
- `EditRenderService`: decode + render.
- `FileSystemEditedExportService`: render → `WicJpegEncoder` (EXIF/XMP copied, orientation reset, EXIF thumbnail
  dropped; falls back to no metadata) → temporary file → move without overwrite.

### UI
- `EditController` keeps every tile's `Recipe` (the **✎** badge, thumbnails, viewer) in step with storage and owns
  presets. A failed save reverts the tile.
- **Develop** (`DevelopViewModel`, **D** in the viewer, or from the grid):
  - sliders render a live preview off the UI thread, latest request wins
  - a change becomes one undo step when the sliders rest for 600 ms; commits run in sequence; leaving the photo or
    the viewer saves first
  - **Ctrl+Z / Ctrl+Y** undo and redo edits while Develop is open (decisions otherwise)
  - **\\** shows before/after; double-clicking a slider resets it
- **Crop** (**R**): shows the whole straightened frame with `CropOverlay`.
  - Drag the inside to move, or a corner or edge to resize.
  - Arrow keys move the crop and Ctrl+arrows resize it.
  - Aspect presets keep their ratio while resizing.
  - A quarter turn resets the crop.
- **Presets:**
  - saved in Develop with chosen groups, and applied there to the current photo
  - applied from the grid through the **Presets…** panel to the selection, with progress, cancel and **Undo last apply**
- **Export → Edited JPEGs:** quality, longest side, and keep metadata. Every photo is rendered (unedited ones are
  re-encoded), so the output is uniform.
- **Thumbnails:** the thumbnail cache entry records the recipe it was rendered for. A stale one stays visible while
  the new one renders.

## Implementation notes (Milestone 08 phase A: Auto tools, no new model)
See [ADR 0007](decisions/0007-auto-editing-tools.md) and [visual-review-m08.md](visual-review-m08.md).
- **Application/Editing/Auto:** `AutoTone`, `AutoStraighten`, `AutoCrop` and `AutoBatch` are pure and
  deterministic. They take pixels or face boxes and the current recipe, and return `AutoSuggestions` (named options,
  each with a recipe and a reason). They never save anything.
- **`IFaceLookup`:** a small port, implemented by `AnalysisController`, that gives Auto crop and zoom to faces the
  stored M05 face boxes and the face-score threshold.
- **`EditGeometry.ToOutput`:** the inverse of `ToSource`. It maps a source point to normalised output coordinates,
  and is used to place crops, guides and face targets after rotation and straighten.
- **Develop:** `DevelopViewModel.Auto.cs` (a partial) owns the suggestions tray. Previews for all options render in
  parallel, the focused option shows on the main photo, and accepting goes through the normal commit chain, so it is
  one undo step. The tray closes on photo change, on close, and when any slider moves.
- **Batch:** `EditController.ApplyAutoAsync` calls `EditService.ApplyAsync` with a per-photo function, so batch Auto
  shares the preset batch rules (progress, cancel, saves every 25, undo).
- **Viewer:** `ZoomPanImage.FocusRequest` zooms to 100 % centred on an area. `ViewerViewModel.StepFace` feeds it.
  `ViewerView` routes the tray's keys, the hold-to-compare key, and gives focus back to the photo when Develop or the
  tray closes.
- **Evaluation:** `tools/FrameForge.Evaluation` builds judging sheets: `light-sheet` (blind A/B), `crop-sheet` (your
  own crops) and `score-sheet`. Each sheet is a static HTML page plus downscaled copies. The answer key stays in
  `sheet.json`, outside the page. Scoring is pure (`SheetScoring`) and reuses `AutoCrop`. See
  [evaluation-m08.md](evaluation-m08.md).

## Dependency direction

- `Core`: depends on no application/UI/infrastructure projects.
- `Application`: depends on Core and defines interfaces (ports).
- `Infrastructure`, `Imaging`, `AI`: implement interfaces and depend inward where possible.
- `App`: composes dependencies and presents application workflows.
- Tests reference the project they test and supporting test packages.

## Key abstractions (introduce when needed)
- `IPhotoImporter`
- `IPhotoRepository`
- `IThumbnailService`
- `IImageDecoder`
- `IPhotoMetadataReader`
- `IFileSystem`
- `ISettingsStore`
- `IImageAnalysisService`
- `IQualityScorer`
- `IModelRuntime`

Do not add all interfaces in Phase 1 without a real use. Introduce them with the first consumer.

## Application workflow
1. User selects a folder.
2. Import coordinator enumerates supported files and reports progress.
3. Metadata reader extracts basic properties.
4. Thumbnail service generates small previews asynchronously.
5. Repository records photo metadata and import state.
6. UI displays virtualised photo tiles.
7. User rates/labels photos; changes persist.
8. Export service writes a selection manifest or copies selected photos to a destination.

## Threading and cancellation
- UI-bound state changes occur on the dispatcher.
- File I/O, decoding, thumbnail creation, and inference run off the UI thread.
- Use `CancellationToken` end-to-end where operations can be cancelled.
- Limit concurrency for decoding and inference; avoid unbounded task creation.
- Progress should be observable and throttled to avoid UI churn.

## Image storage and cache
- Source files stay in their original location.
- Database stores absolute path initially; consider stable file identity and relocation handling later.
- Thumbnails live in an app-managed cache, keyed by file identity plus relevant decode/version settings.
- Cache is disposable and rebuildable.
- Avoid retaining decoded bitmaps indefinitely; use bounded cache and explicit disposal.

## Error handling
- Expected per-file failures should be represented as import/analyse outcomes and surfaced in the UI.
- Unexpected errors should be logged and surfaced with actionable messages.
- Never silently skip corrupt or inaccessible files.
- A failed file should not abort the entire folder import unless the failure is systemic.

## Architecture decision records
Existing: 0001 foundation stack · 0002 imaging and thumbnails · 0003 culling and export · 0004 deterministic
analysis · 0005 local AI models · 0006 non-destructive editing · 0007 Auto editing tools.

Record major decisions in `docs/decisions/` using:
- Context
- Decision
- Alternatives
- Consequences
- Date
