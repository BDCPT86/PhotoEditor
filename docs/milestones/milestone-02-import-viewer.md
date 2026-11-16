# Milestone 02 — Import and Viewer

## Goal
Import a folder of JPEGs, generate thumbnails, and browse them in a responsive UI.

## Deliverables
- Folder picker
- JPEG discovery with configurable recursive/non-recursive behavior
- Basic metadata extraction (dimensions, timestamps, EXIF where practical)
- Import progress and cancellation
- Resumable/idempotent import behavior
- Thumbnail cache with bounded memory
- Virtualised photo grid
- Single-photo viewer
- Zoom/pan and next/previous navigation
- Missing/corrupt file handling and visible error state

## Acceptance criteria
- Import a test folder with hundreds/thousands of JPEGs without freezing UI
- Cancel import cleanly
- Re-import does not create duplicate records
- Thumbnails are cached and regenerated if missing
- Full-resolution images are not loaded for all grid tiles
- Tests cover enumeration, import idempotency, and error handling

## Prompt
Implement only Milestone 02. First inspect the existing foundation and propose a plan. Keep image decoding and file access outside ViewModels. Use asynchronous operations, cancellation, bounded concurrency, and a virtualised grid. Do not add AI analysis, culling decisions, or RAW support yet. Build, test, and document the result.
