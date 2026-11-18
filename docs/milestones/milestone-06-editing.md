# Milestone 06 — Non-Destructive Editing

> **Status (2026-11-16):** Done. See [ADR 0006](../decisions/0006-non-destructive-editing.md) and [visual-review-m06.md](../visual-review-m06.md).

## Goal
Add a recipe-based editing foundation and preview/export pipeline.

## Deliverables
- Edit recipe stored separately from original
- Exposure, white balance, contrast, highlights/shadows, saturation
- Crop/straighten model
- Preview rendering
- Presets and batch application
- Export renderer with output settings
- Undo/reset recipe controls

## Acceptance criteria
- Original files remain byte-for-byte untouched
- Recipe saves and reloads accurately
- Preview and export use consistent settings
- Batch operation has progress/cancellation
- Tests cover recipe persistence and parameter validation
- Visual regression/sample review documented

## Prompt
Implement only Milestone 06. Design a non-destructive edit recipe and rendering pipeline. Do not claim RAW-quality rendering from JPEG-only processing. Keep rendering independent of UI. Add sample-based visual validation and tests. Build and document before moving on.
