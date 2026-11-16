# 0001 — Foundation stack and project split

**Date:** 2026-11-16
**Status:** Accepted (Milestone 01)

## Context
Milestone 01 needs a compiling solution, DI, logging, SQLite persistence with migrations, settings, a WPF shell with navigation, and a test baseline. The architecture document proposes six source projects; the brief asks for the smallest sensible structure. At the time of writing, EF Core and Microsoft.Extensions 11.x target `net11.0` only, while the project targets .NET 10 LTS.

## Decision
- **Target** .NET 10 LTS (SDK 10.0.404 pinned in `global.json`, `latestFeature` roll-forward).
- **Projects:** Core, Application, Infrastructure, App, plus Application.Tests and Infrastructure.Tests. Imaging and AI projects are deferred to their milestones.
- **Packages** (central management in `Directory.Packages.props`):
  - EF Core Sqlite/Design **10.0.15** and Microsoft.Extensions **10.0.15** — the latest line compatible with .NET 10.
  - Microsoft.Extensions.Hosting for DI/lifetime/logging integration rather than wiring `ServiceCollection` and logging by hand.
  - Serilog.Extensions.Hosting 10.0.0 + Serilog.Sinks.File 7.0.0.
  - CommunityToolkit.Mvvm 8.4.2.
  - xunit.v3 4.0.3 on Microsoft.Testing.Platform (opted in via `global.json`); no VSTest packages.
- **Settings** stored as key/JSON rows in a single `AppSettings` table; typing, defaults, and validation live in the Application layer.
- **DbContext access** through `IDbContextFactory`; SQLite work moved to the thread pool because Microsoft.Data.Sqlite has no true async I/O.
- **Analyzers:** `AnalysisLevel=latest-recommended` with warnings as errors (CA1707 relaxed for test names only).

## Alternatives
- *Six projects now:* rejected — Imaging and AI would be empty shells.
- *Single test project:* workable, but separate projects keep test dependencies aligned with the layer under test and make room for persistence integration tests.
- *Plain `ServiceCollection` without Generic Host:* fewer packages, but loses standard lifetime/disposal and logging integration that EF Core already expects.
- *Serilog `retainedFileCountLimit`:* count-based; the product setting is age-based in days, so a small `LogFileRetention` class handles it.
- *xunit.v3 3.x with VSTest:* would avoid the `global.json` runner opt-in but uses the older test pipeline.

## Consequences
- Upgrading to .NET 11/12 later requires moving EF Core and Microsoft.Extensions together.
- IDE test explorers must support Microsoft.Testing.Platform (not verified against a specific Visual Studio version in this milestone).
- Adding a setting is a one-line `SettingDefinition<T>` in Core; no migration is needed for new keys.
