# FrameForge AI

A Windows-first, local-first photography application focused on photo culling and, later, editing.

**Status:** Milestone 04 (Deterministic analysis). Import folders of JPEGs, cull them with the keyboard
(keep/reject/review, ratings, colour labels), filter and sort, compare photos side by side, and export the selection.
**Analyze** measures sharpness and exposure and finds exact and near-duplicates. These are explainable heuristics,
not AI. No AI features yet.

## Using it

1. **Library → Import folder…** (Ctrl+I) and choose a folder. "Include subfolders" is remembered.
2. Import reads file metadata only; thumbnails are created as tiles scroll into view. Cancel at any time, and run
   the same import again to continue where it stopped. Re-importing never creates duplicates.
3. **Cull:** press **P** (keep), **X** (reject), **V** (review), **U** (unset), **1–5** (stars), **6–9** (labels).
   In the grid, keys apply to every selected photo (Shift/Ctrl+click, Shift+arrows). In the viewer (Enter), they apply
   to the current photo, and the next photo opens automatically after a Keep/Reject/Review/Unset (Settings → Culling).
   **Ctrl+Z / Ctrl+Y** undo and redo. Press **F1** for all shortcuts.
4. **Work through what's left:** choose **Show → Unset**. Each decided photo leaves the view, and selection moves on.
5. **Compare:** select 2–4 photos and press **C**. ← / → choose the photo that decision keys apply to.
6. **Export…:** copy every Keep photo (or the current view) to a folder, or save a CSV manifest of the selection.
7. **Analyze:** measures every photo once and remembers the results; later runs only analyse new or changed photos.
   Tiles get badges: **≈ blurry?**, **◐ clip**, **▣ n** (in a group of n similar photos), **◆ sharpest** (the sharpest
   in its group), and **= duplicate** (identical file). Use the **ANALYSIS** filter row to review them. Press **G** to
   select a photo's group (then **C** to compare), and **I** to see the exact numbers and why each badge was given.
   Thresholds are in **Settings → Analysis** and apply instantly. The defaults are starting points: tune them on your
   own photos. Analysis never changes decisions or deletes anything.

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

## Where data is stored

| What | Location |
|---|---|
| Database (SQLite) | `%LOCALAPPDATA%\FrameForge\frameforge.db` |
| Logs (daily rolling) | `%LOCALAPPDATA%\FrameForge\logs\frameforge-YYYYMMDD.log` |
| Thumbnail cache | `%LOCALAPPDATA%\FrameForge\thumbnails\` |

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
| Library | F5 | Re-apply filter and sort |
| Library | Ctrl+I | Import folder |
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
  FrameForge.Analysis/        Deterministic image metrics and grouping on pixel buffers (no WPF, no AI)
  FrameForge.Application/     Use cases and ports (import, library, settings, thumbnail keys)
  FrameForge.Infrastructure/  SQLite/EF Core, file system scanning, thumbnail disk cache, logging, app data paths
  FrameForge.Imaging/         JPEG metadata, thumbnails, and display decoding via Windows Imaging Component
  FrameForge.App/             WPF composition root, shell, grid, viewer, view models, theme
tests/
  FrameForge.Analysis.Tests/  (synthetic image fixtures, no binary files)
  FrameForge.Application.Tests/
  FrameForge.Infrastructure.Tests/
  FrameForge.Imaging.Tests/   (Windows only; generates its JPEG fixtures at test time)
  FrameForge.App.Tests/       (Windows only)
docs/                         Architecture, roadmap, milestones, design system, decisions
```

See [docs/architecture.md](docs/architecture.md) for boundaries and [docs/roadmap.md](docs/roadmap.md) for phases.

## Project documents

- `CLAUDE.md` — project instructions for Claude Code
- `docs/architecture.md` — architecture and boundaries (reflects the current implementation)
- `docs/decisions/` — architecture decision records
- `docs/roadmap.md` — phased roadmap and acceptance gates
- `docs/milestones/` — implementation briefs
- `docs/database-schema.md` — data model proposal
- `docs/ui-design-system.md` — UX principles and visual direction
- `docs/ai-models.md` — AI model strategy and licensing checks
