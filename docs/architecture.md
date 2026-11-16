# Architecture

## Goals
- Local-first desktop experience; no cloud dependency for core culling.
- Responsive interface over large photo collections.
- Original images remain untouched.
- AI capabilities are optional, replaceable, and separately testable.
- Domain/application logic can be tested without launching WPF.

## Solution structure

### Current (Milestone 01)

```text
FrameForge.sln
global.json                   SDK pin (10.0.404, latestFeature) + Microsoft.Testing.Platform runner
Directory.Build.props         net10.0 defaults: nullable, warnings-as-errors, latest-recommended analyzers
Directory.Packages.props      Central package versions
dotnet-tools.json             Local tools (dotnet-ef 10.0.15)
src/
  FrameForge.Core/            Setting definitions (SettingDefinition<T>, AppSettings, AppPage). No dependencies.
  FrameForge.Application/     Ports and use cases: ISettingsStore, ISettingsService/SettingsService
  FrameForge.Infrastructure/  EF Core/SQLite (FrameForgeDbContext, migrations, DatabaseMigrator),
                              SqliteSettingsStore, LogFileRetention, AppDataPaths
  FrameForge.App/             WPF (net10.0-windows): composition root, shell, page views/view models, theme
tests/
  FrameForge.Application.Tests/     SettingsService and SettingDefinition rules (in-memory store)
  FrameForge.Infrastructure.Tests/  Real SQLite file: migrations, settings persistence, log retention
```

### Planned additions (introduce with their milestone)

```text
src/FrameForge.Imaging/   image decoding, thumbnails, metadata, transforms   (Phase 2)
src/FrameForge.AI/        model loading, inference, scoring                   (Phase 5)
tests/FrameForge.Core.Tests/, FrameForge.Imaging.Tests/, FrameForge.AI.Tests/  as those projects gain logic
```

`FrameForge.Core.Tests` was not created in Milestone 01 because Core contains only a small setting-definition type; its rules are tested from `FrameForge.Application.Tests`. Preserve boundaries, not project count.

## Implementation notes (Milestone 01)

### Composition root and startup
- `App.xaml.cs` is the composition root. `CompositionRoot.BuildHost` uses the .NET Generic Host (`Microsoft.Extensions.Hosting`) for DI, lifetimes, and `ILogger<T>`; Serilog is the only logging provider.
- Each layer exposes one registration extension: `AddFrameForgeApplication()`, `AddFrameForgeInfrastructure(paths)`. View models and `MainWindow` are registered in the App project.
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
- Current settings: `app.startPage` (Library/Settings/About, default Library) and `logging.retentionDays` (1–365, default 14). Both apply on next launch.

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
