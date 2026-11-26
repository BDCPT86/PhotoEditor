# Roadmap

Each phase must build, test, and be documented before the next phase starts. Each phase below says what was planned,
what was delivered, and what was left out or done differently. The milestone briefs in `docs/milestones/` and the
ADRs in `docs/decisions/` hold the details.

**Where things stand (2026-11-26):** Phases 1–8 are done. Phase 8 was done before Phase 7, because it depends only on
Phases 5 and 6. Nothing after Phase 8 is planned yet; see [Not yet scheduled](#not-yet-scheduled).

## Phase 1 — Foundation (M01; done 2026-11-16)
- Solution/projects and build configuration
- WPF shell, navigation, MVVM, DI
- Logging and settings
- SQLite setup and migrations
- Test baseline and docs

**Delivered as planned.** See [ADR 0001](decisions/0001-foundation-stack.md).

**Gate:** clean build, tests pass, app launches, navigation works.

## Phase 2 — Import and viewer (M02; done 2026-11-16)
- Folder picker and supported-file discovery (JPEG initially)
- Metadata extraction
- Thumbnail generation and cache
- Virtualised grid
- Single-photo viewer, zoom/pan, keyboard navigation
- Progress, cancellation, recoverable errors

**Delivered as planned, for JPEG.** Import is resumable and doesn't create duplicates. See
[ADR 0002](decisions/0002-imaging-and-thumbnails.md).

**Gate:** import a test folder without UI freeze; thumbnails persist/rebuild; missing/corrupt files handled.

## Phase 3 — Culling workflow (M03; done 2026-11-16)
- Keep / Reject / Review
- Ratings and colour labels
- Filters and sorting
- Compare view
- Selection export (manifest and/or copy)
- Persist decisions

**Delivered as planned,** with undo and redo for decisions. See [ADR 0003](decisions/0003-culling-and-export.md).

**Gate:** user can complete an import-to-export culling workflow and reopen the project with decisions intact.

## Phase 4 — Deterministic analysis (M04; done 2026-11-16)
- Sharpness/blur metrics
- Exposure and clipping indicators
- Perceptual hashes
- Near-duplicate grouping
- Explainable score components and configurable weights

**Delivered:** sharpness, clipping, exact and near duplicates, burst groups, versioned and cached results.
- **Differs from the plan:** each finding shows its value, threshold and method, and the thresholds can be tuned. There
  is no combined score with weights.
- See [ADR 0004](decisions/0004-deterministic-analysis.md).

**Gate:** analysis is repeatable, cached, cancellable, and clearly labelled as heuristic—not AI certainty.

## Phase 5 — AI-assisted culling (M05; done 2026-11-16)
- Face/subject detection where useful
- Image embeddings and semantic search
- Optional eye-closure detection
- Quality model evaluation
- Ranking recommendations with confidence and explanations
- Model settings and model download/licence workflow

**Delivered:**
- face detection (YuNet)
- scene embeddings (DINOv2-small) that group similar shots
- "soft faces"
- a suggested pick per group, with a confidence band and its reasons
- the model catalogue, with consent before any download and SHA-256 checks

All AI output is advisory and off by default. See [ADR 0005](decisions/0005-local-ai-models.md) and the
[benchmark](benchmarks/m05-ai-benchmark.md).

**Not done:**
- semantic (text) search
- eye-closure detection ("soft faces" checks the sharpness of faces instead)
- measuring the models' quality on a human-reviewed set

**Gate:** benchmark on a consented/licensed dataset; measure quality, speed, memory, and false positives.
**Partly met:** speed and memory are benchmarked. Quality is not, and the app labels AI output "not yet evaluated".

## Phase 6 — Editing foundation (M06; done 2026-11-16)
- Non-destructive edit recipe
- Exposure, white balance, contrast, highlights/shadows, saturation
- Preview pipeline
- Presets and batch application
- Export rendering

**Delivered as planned,** plus vibrance, crop, straighten, quarter turns and undo. See
[ADR 0006](decisions/0006-non-destructive-editing.md) and [visual-review-m06.md](visual-review-m06.md).

**Gate:** original files untouched; recipe round-trips; output is visually reviewed.

## Phase 7 — RAW and professional workflow (M07; done 2026-11-26)
- Evaluate LibRaw integration and supported camera formats
- RAW preview/render pipeline
- Export profiles and metadata preservation
- Catalog/project management improvements
- Installer, crash reporting opt-in, performance profiling
- Commercial licensing and model licence review

**Delivered:**
- **RAW:** Canon CR3 through LibRaw 0.22.3 ([ADR 0020](decisions/0020-raw-through-libraw-previews.md)), tested with
  the Canon EOS R50.
- **Export:** file-name patterns, a metadata choice and saved profiles
  ([ADR 0017](decisions/0017-export-naming-and-metadata.md)).
- **Moved folders and offline drives** ([ADR 0016](decisions/0016-moved-folders-and-offline-drives.md)).
- **Performance:** viewer look-ahead, released memory, a faster grid and a cache limit
  ([report](benchmarks/m07-performance.md)).
- **Installer and crash reports:** an installer, manual updates, backups before upgrades, and local crash reports
  ([ADR 0019](decisions/0019-installer-updates-crash-reports.md)).
- **Licences:** a licence and privacy audit with ONNX Runtime telemetry off
  ([ADR 0018](decisions/0018-licences-and-no-telemetry.md), [checklist](licence-checklist.md)).
- **Added on request:** the logo and loading screen.

**Differs from the plan:**
- **RAW preview and render:** RAW files are shown, analysed and edited through the full-size JPEG the camera stores in
  them. There is no develop from the sensor data.
- **Formats:** only CR3 is imported. Other formats are counted and reported, not imported.
- **Catalogue management:** only relocation and offline drives. There are no collections or projects.
- **Crash reporting:** local only. Nothing is ever sent, so there is no opt-in upload to choose.

**Gate:** none was written in this roadmap. The M07 brief's acceptance criteria and its completion review take its
place.

## Phase 8 — AI-assisted editing (M08; done 2026-11-24)
- Deterministic Auto settings first; a model only if it measurably does better
- Subject, sky and background masks for local adjustments (recipe schema v2)
- Auto-straighten, auto-crop to subject (rule of thirds, chosen aspect ratio), batch reframe, zoom to faces
- Optional learned enhancement, noise reduction or upscaling, only with clearly licensed weights
- Can be scheduled before Phase 7; it depends only on Phases 5 and 6

**Delivered:**
- Auto light, level and crop, with no model ([ADR 0007](decisions/0007-auto-editing-tools.md))
- Batch Auto and zoom to faces
- Recipe schema v2 ([ADR 0009](decisions/0009-recipe-schema-v2.md))
- Subject masks: YOLOX-s finds people and MobileSAM masks them, refined with clicks
  ([ADR 0008](decisions/0008-subject-mask-model.md))
- A sky mask without a model, and background as the inverse of subject
- Rendering of local adjustments ([ADR 0011](decisions/0011-local-adjustment-rendering.md))
- The Develop masks UI ([ADR 0012](decisions/0012-develop-masks-ui.md))
- Masks in presets and batch ([ADR 0013](decisions/0013-batch-masks.md))
- Blind human evaluations of Auto light, Auto crop and the masks ([evaluation-m08.md](evaluation-m08.md),
  [ADR 0014](decisions/0014-mask-speed-and-evaluation.md))

**Not done:** the optional learned enhancement, noise reduction and upscaling. No model with clean licensing was
evaluated.

**Gate:** works with no model installed; suggestions are applied only by the user and are undoable; mask quality is
measured on a human-reviewed set; preview matches export. **Met.**

## Not yet scheduled
Left out of earlier phases, or suggested by them. None of it is planned or approved.
- **RAW:** a develop from the sensor data, for highlight and shadow recovery. Other RAW formats and cameras, each
  tested before it is turned on.
- **Library:** collections or projects.
- **AI culling:**
  - measure face detection, grouping and the suggested pick on a human-reviewed set
  - semantic (text) search
  - eye-closure detection
- **AI editing:** learned enhancement, noise reduction or upscaling, if a model with clean licensing measurably helps.
- **Before giving FrameForge to anyone else:**
  - ship LibRaw's source, or a written offer to provide it
  - confirm the Visual C++ runtime may be redistributed ([licence checklist](licence-checklist.md))

## Explicit non-goals for early versions
- Cloud sync
- Mobile apps
- Full Lightroom replacement
- Training a foundation model from scratch
- Automatic destructive edits
- Unverified claims of AI accuracy
