# FrameForge AI

A Windows-first, local-first photography application focused on photo culling and, later, editing.

**Status:** Milestone 01 (Foundation) — application shell, settings, logging, and database. Photo import is not implemented yet.

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

The database is created and migrated automatically on startup. Deleting the `FrameForge` folder resets the app. The About page also shows these paths.

## Keyboard

| Shortcut | Action |
|---|---|
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Library / Settings / About |
| ↑ / ↓ in the navigation list | Move between pages |
| Ctrl+S (Settings page) | Save settings |

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
  FrameForge.Core/            Domain types (setting definitions); no dependencies
  FrameForge.Application/     Use cases and ports (ISettingsStore, SettingsService)
  FrameForge.Infrastructure/  SQLite/EF Core, settings store, log retention, app data paths
  FrameForge.App/             WPF composition root, shell, views, view models, theme
tests/
  FrameForge.Application.Tests/
  FrameForge.Infrastructure.Tests/
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
