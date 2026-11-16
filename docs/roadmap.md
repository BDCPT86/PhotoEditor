# Roadmap

Each phase must build, test, and be documented before the next phase starts.

## Phase 1 — Foundation
- Solution/projects and build configuration
- WPF shell, navigation, MVVM, DI
- Logging and settings
- SQLite setup and migrations
- Test baseline and docs

**Gate:** clean build, tests pass, app launches, navigation works.

## Phase 2 — Import and viewer
- Folder picker and supported-file discovery (JPEG initially)
- Metadata extraction
- Thumbnail generation and cache
- Virtualised grid
- Single-photo viewer, zoom/pan, keyboard navigation
- Progress, cancellation, recoverable errors

**Gate:** import a test folder without UI freeze; thumbnails persist/rebuild; missing/corrupt files handled.

## Phase 3 — Culling workflow
- Keep / Reject / Review
- Ratings and colour labels
- Filters and sorting
- Compare view
- Selection export (manifest and/or copy)
- Persist decisions

**Gate:** user can complete an import-to-export culling workflow and reopen the project with decisions intact.

## Phase 4 — Deterministic analysis
- Sharpness/blur metrics
- Exposure and clipping indicators
- Perceptual hashes
- Near-duplicate grouping
- Explainable score components and configurable weights

**Gate:** analysis is repeatable, cached, cancellable, and clearly labelled as heuristic—not AI certainty.

## Phase 5 — AI-assisted culling
- Face/subject detection where useful
- Image embeddings and semantic search
- Optional eye-closure detection
- Quality model evaluation
- Ranking recommendations with confidence and explanations
- Model settings and model download/licence workflow

**Gate:** benchmark on a consented/licensed dataset; measure quality, speed, memory, and false positives.

## Phase 6 — Editing foundation
- Non-destructive edit recipe
- Exposure, white balance, contrast, highlights/shadows, saturation
- Preview pipeline
- Presets and batch application
- Export rendering

**Gate:** original files untouched; recipe round-trips; output is visually reviewed.

## Phase 7 — RAW and professional workflow
- Evaluate LibRaw integration and supported camera formats
- RAW preview/render pipeline
- Export profiles and metadata preservation
- Catalog/project management improvements
- Installer, crash reporting opt-in, performance profiling
- Commercial licensing and model licence review

## Phase 8 — AI-assisted editing (draft brief: `docs/milestones/milestone-08-ai-editing.md`)
- Deterministic Auto settings first; a model only if it measurably does better
- Subject, sky and background masks for local adjustments (recipe schema v2)
- Auto-straighten, auto-crop to subject (rule of thirds, chosen aspect ratio), batch reframe, zoom to faces
- Optional learned enhancement, noise reduction or upscaling, only with clearly licensed weights
- Can be scheduled before Phase 7; it depends only on Phases 5 and 6

**Gate:** works with no model installed; suggestions are applied only by the user and are undoable; mask quality is
measured on a human-reviewed set; preview matches export.

## Explicit non-goals for early versions
- Cloud sync
- Mobile apps
- Full Lightroom replacement
- Training a foundation model from scratch
- Automatic destructive edits
- Unverified claims of AI accuracy
