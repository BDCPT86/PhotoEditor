# FrameForge AI

A Windows-first, local-first photography application focused on photo culling and, later, editing.

**Status:** Milestone 02 (Import and viewer). Import folders of JPEGs, browse them in a virtualised grid, and view photos one at a time with zoom and pan. Culling decisions (keep/reject, ratings) are not implemented yet.

## Using it

1. **Library → Import folder…** (Ctrl+I) and choose a folder. "Include subfolders" is remembered.
2. Import reads file metadata only; thumbnails are created as tiles scroll into view. Cancel at any time, and run
   the same import again to continue where it stopped. Re-importing never creates duplicates.
3. Select a photo and press **Enter** (or double-click) to open the viewer.

Originals are opened read-only and are never modified, moved, or copied. Unreadable files are listed as
**Unreadable**, and files deleted since the last import are listed as **Missing** the next time that folder is imported.

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

| Where | Shortcut | Action |
|---|---|---|
| Anywhere | Ctrl+1 / Ctrl+2 / Ctrl+3 | Library / Settings / About |
| Library | Ctrl+I | Import folder |
| Grid | Arrow keys | Move selection (↑/↓ by row) |
| Grid | Enter / double-click | Open viewer |
| Viewer | ← / → | Previous / next photo |
| Viewer | Home / End | First / last photo |
| Viewer | Z or double-click | Toggle fit ↔ 100% |
| Viewer | + / − / 0 | Zoom in / out / fit |
| Viewer | Mouse wheel / drag | Zoom around the cursor / pan |
| Viewer | Esc | Back to the grid |
| Settings | Ctrl+S | Save settings |

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
  FrameForge.Core/            Domain types (settings, photos, import results); no dependencies
  FrameForge.Application/     Use cases and ports (import, library, settings, thumbnail keys)
  FrameForge.Infrastructure/  SQLite/EF Core, file system scanning, thumbnail disk cache, logging, app data paths
  FrameForge.Imaging/         JPEG metadata, thumbnails, and display decoding via Windows Imaging Component
  FrameForge.App/             WPF composition root, shell, grid, viewer, view models, theme
tests/
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
