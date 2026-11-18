# Milestone 01 — Foundation

> **Status (2026-11-16):** Done. See [ADR 0001](../decisions/0001-foundation-stack.md).

## Goal
Create a clean, compiling solution foundation and a minimal WPF shell.

## Read first
- `CLAUDE.md`
- `docs/architecture.md`
- `docs/roadmap.md`
- `docs/ui-design-system.md`

## Deliverables
- Solution and projects using a sensible, minimal structure
- Central package/version management where appropriate
- Dependency injection and composition root
- Serilog configured
- SQLite/EF Core setup with first migration (settings table only is acceptable)
- Settings service with defaults
- WPF main window and navigation shell
- MVVM wiring
- Basic About/Settings placeholder pages if useful
- Unit test project and at least one meaningful test
- README with build/run instructions
- Architecture document updated to reflect actual implementation

## Acceptance criteria
- `dotnet build` succeeds
- `dotnet test` succeeds
- Application launches
- Navigation works without code-behind business logic
- No long-running work on UI thread
- Logging writes to a local app log
- Settings persist and can be read back
- No unnecessary future-phase functionality

## Prompt
Read the project instructions and architecture docs. Inspect the repository and SDK availability. Propose the smallest implementation plan and identify any version/package risks. After approval, implement only Milestone 01. Build and test it, report actual output, and update documentation. Do not begin photo import.
