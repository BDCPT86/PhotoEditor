# 0006 — Non-destructive editing: recipe, renderer, storage and export

**Date:** 2026-11-16
**Status:** Accepted (Milestone 06)

## Context
Milestone 06 adds editing without ever touching the original files. The requirements:
- a stored edit "recipe"
- exposure, white balance, contrast, highlights/shadows and saturation
- crop and straighten
- a preview that matches the export
- presets applied in batches with progress and cancel
- undo and reset

Only JPEGs are imported so far, so editing works on 8-bit data. This ADR does not claim RAW-quality results.

## Decision

### Recipe
`Core.Editing.EditRecipe` is an immutable record:
- **Fields:** exposure (EV), temperature, tint, contrast, highlights, shadows, saturation, vibrance, quarter turns, straighten, and a crop rectangle.
- **Neutral:** every field is 0 when neutral, and a neutral recipe renders the photo bit-for-bit unchanged (tested).
- **Versioning and validation:** recipes carry a schema version. `Validate()` (ranges, NaN/∞, crop bounds) runs on save, on load and before every render.
- **Crop coordinates:** the crop is normalised to the largest rectangle with the photo's aspect ratio that fits inside the straightened picture. Any crop inside 0–1 therefore has no blank corners, and the same recipe frames identically at any resolution.

### Renderer
A new project, `FrameForge.Rendering`, holds pure managed code with no WPF and no native dependencies.
- **Geometry:** each output pixel is mapped back to the source in the order quarter turns → straighten → crop, then sampled bilinearly.
- **Tone:** pixels are converted to linear light (sRGB table), then:
  - white-balance and exposure gains
  - a tone curve on perceptual lightness (shadows/highlights bumps, then contrast), applied as a gain so hue is kept
  - saturation/vibrance as chroma scaling in OKLab
- **Encoding:** back to sRGB through a table indexed by √linear, so shadows stay precise.
- **Rules:** every curve is monotonic and keeps black at 0 and white at 1 (tested).
- **Speed:** rows are processed in parallel. A 2048 px preview takes about 5–25 ms and a 24 MP render about 250 ms on the development machine.
- **Consistency:** preview, export and edited thumbnails all call `EditRenderer.Render`; only the source resolution differs. A test checks that a preview matches the downscaled export (mean difference < 2 levels).

### Storage
Two new tables, added by migration `AddPhotoEditsAndPresets`:
- `PhotoEdits` (one row per edited photo, JSON recipe, cascade delete). A neutral recipe deletes its row.
- `EditPresets` (name unique case-insensitively, the groups it carries, JSON recipe).

Unreadable rows are logged and ignored, never fatal.

### Presets
A preset carries chosen groups (exposure, white balance, tone, colour, geometry) and changes only those. Batch apply:
- saves in batches of 100, with progress and cancel
- keeps already-saved batches when cancelled
- records the before and after of every photo, so **Undo last apply** restores them exactly

### Undo
Each photo keeps its own undo history while Develop is open. A change becomes one undo step when the sliders rest for 600 ms, so dragging a slider is one step. Commits run one at a time, and a flush waits for a save already in progress. A failed save reverts the photo and is not recorded as a step.

### Export
`FileSystemEditedExportService` renders each photo and writes a JPEG through WIC:
- quality 50–100 (default 92) and an optional longest side; it never upscales
- the original's EXIF/XMP is copied with orientation reset to 1, because the pixels are already upright, and the embedded EXIF thumbnail (which shows the unedited photo) is removed
- the same safety rules as copying: write to a temporary file, move without overwrite, use a `(n)` suffix on name clashes
- one photo at a time to keep memory bounded

## Alternatives
- **Writing XMP sidecars or editing in place:** rejected. Originals must stay byte-identical, and sidecars belong with a later decision on interoperability.
- **ImageSharp, Magick.NET or OpenCV for processing:** not needed for per-pixel tone/colour plus resampling. The managed code is about 400 lines, fully tested and fast enough. Native libraries would add deployment and licence surface. Revisit for local adjustments or RAW (M07).
- **16-bit or float pipelines through the whole app:** the sources are 8-bit JPEGs, so intermediate maths is done in float per pixel, while buffers stay 8-bit to bound memory. Revisit with RAW.
- **GPU (WPF effects or shaders):** rejected. It would make preview and export diverge, which the brief rules out.
- **Multiplicative shadows/highlights (first draft):** replaced after the visual review. "Highlights −100" turned clipped whites grey, a foggy look; negative contrast did the same. The bump-shaped curves keep the end points.

## Consequences
- Edits are cheap to store and fully reversible. The originals are verified byte-identical in tests (export, encoder, persistence).
- **8-bit limits:** clipped highlights cannot be recovered, and strong pushes can band. Highlights/shadows and contrast are global, with no local masks. Temperature and tint are shifts relative to how the photo was shot, because a JPEG carries no raw white balance.
- **Colour management:** colour profiles follow WIC's default decoding, and output is tagged and treated as sRGB. Wide-gamut sources are not specially handled.
- **Preview resolution:** Develop previews at 2048 px, so zooming to 100% while developing shows the preview. The viewer shows the full-size edit once Develop is closed.
- **Editing the recipe later:** AI-assisted editing (auto settings, masks) can fill or extend the recipe; new fields need a schema version bump and a migration of stored JSON.
- The renderer's version (`render-v1`) is pinned by golden-pixel tests. A deliberate change requires a visual review, new golden values and a version bump.
