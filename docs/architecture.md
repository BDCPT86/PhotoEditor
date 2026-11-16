# Architecture

## Goals
- Local-first desktop experience; no cloud dependency for core culling.
- Responsive interface over large photo collections.
- Original images remain untouched.
- AI capabilities are optional, replaceable, and separately testable.
- Domain/application logic can be tested without launching WPF.

## Solution structure

### Current (Milestone 03)

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
  FrameForge.Application/     Ports and use cases: settings, import, library, thumbnails (ThumbnailKey),
                              decisions (IDecisionService/DecisionService, DecisionHistory, IPhotoDecisionRepository),
                              export (IExportService, ExportNamePlanner, ManifestWriter)
  FrameForge.Infrastructure/  EF Core/SQLite (DbContext, migrations, photo/decision/settings repositories),
                              FileSystemPhotoEnumerator, DiskThumbnailService, FileSystemExportService,
                              LogFileRetention, AppDataPaths
  FrameForge.Imaging/         WIC via WPF imaging (net10.0-windows): metadata, thumbnails, full and display-size decode
  FrameForge.App/             WPF: composition root, shell, Library (grid, filters, viewer, compare, export, help),
                              DecisionController, CullingShortcuts, PhotoGridListBox, ZoomPanImage, ThumbnailLoader
tests/
  FrameForge.Application.Tests/     Settings, import, decision rules, undo history, filter/sort determinism,
                                    export name planning, CSV manifest
  FrameForge.Infrastructure.Tests/  Real SQLite + file system: migrations, repositories (incl. decisions across restart),
                                    enumerator, thumbnail cache, import end-to-end, export safety
  FrameForge.Imaging.Tests/         JPEG fixtures generated in-test: EXIF parsing, orientation, thumbnails, corrupt files
  FrameForge.App.Tests/             Culling workflow through real view models (grid, viewer, compare, export),
                                    keyboard map, viewer, thumbnail loader, LRU cache, zoom math
```

### Planned additions (introduce with their milestone)

```text
src/FrameForge.AI/        model loading, inference, scoring                   (Phase 5)
tests/FrameForge.Core.Tests/, FrameForge.AI.Tests/  as those projects gain logic
```

`FrameForge.Core.Tests` does not exist yet: Core's rules are small and are tested from the Application and App test projects. Preserve boundaries, not project count.

### Project dependencies

```text
App ──► Application ──► Core
 │  ──► Infrastructure ──► Application
 └────► Imaging ───────► Application
```

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
- Current settings: `app.startPage` (Library/Settings/About, default Library) and `logging.retentionDays` (1–365, default 14), both applied on next launch; `import.includeSubfolders` (default true) and `import.lastFolder`, remembered from the Library page; `viewer.autoAdvance` (default true), on the Settings page.

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
Record major decisions in `docs/decisions/` using:
- Context
- Decision
- Alternatives
- Consequences
- Date
