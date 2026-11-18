# 0012 — Develop's Masks section and Refine

**Date:** 2026-11-18
**Status:** Accepted (Milestone 08 phase B, step B5). Visual review on the user's photos pending.

## Context
Masks can be made (ADR 0010) and drawn (ADR 0011). Develop needs a way to add them, adjust each one, see what is
selected and fix it with clicks, following the project's principles:
- The photo is the hero.
- Suggestions are never imposed, and everything is undoable.
- Every judgement explains itself.
- The app is keyboard-first.
- Meaning is never carried by colour alone.

## Decision
- **Where:** a **Masks** section between Colour and Crop, so the overall look comes first and areas are refined after.
- **Adding:** Subject, Sky and Background (Subject inverted), up to 8 masks.
  - Subject and Background are disabled, with the reason shown (models missing or failing to load), when the subject
    models cannot run. Sky needs no model.
  - A new mask is selected and shown on the photo at once, so you see what was picked.
- **Rows:**
  - each mask's name and what was found ("2 people", "Sky covers 31 % of the photo")
  - Invert (◑) and Delete (✕) buttons, with tooltips and accessible names
  - **While computing:** a thin progress bar with **Cancel**; the photo keeps showing the edit without that mask.
  - **When unavailable:** ⚠ and the reason, with **Retry**.
  - The selected row has an accent bar and a bold name, not colour alone.
- **The selected mask's sliders:**
  - The eight local sliders, in the same order and with the same ranges as the global ones, grouped as light and
    colour. Double-click resets one.
  - Sliders move freely while a mask is still computing; that does not restart it.
- **Show (O):** the preview darkens and greys everything outside the mask and outlines its 50 % edge in amber, the
  look graded on the B1 sheets. It is painted into the preview image, so it follows zoom and pan.
- **Refine (M):**
  - Click adds, Alt+click or a right-click removes, and Esc stops. A hint on the photo says so while refining.
  - Zoom and pan keep working: a drag pans, only a click without a drag places a point, and re-renders keep the zoom
    while refining. Double-click zoom is off in this mode (Z still works).
  - Each click is marked with a ring holding + or −.
  - Clicks are mapped back through crop, straighten and rotation to the upright photo, where masks live, and follow
    the rules of ADR 0010: click a person to add to them, Alt+click a person to remove them.
  - **Clear clicks** removes them all. Refine and Crop mode turn each other off.
- **Undo:** masks are part of the recipe, so adding, deleting, inverting, clicking and each slider resting point are
  normal Develop undo steps, with no separate history.
- **Clicks are fast:** the mask service keeps the last decoded photo, the subject generator its people, and MobileSAM
  its image encoding. A click therefore runs only the decoder: 43 ms for one prompt on a synthetic test image (the
  real-model test). A subject with several people runs one decode per person.
- **Presets:** a **Masks** checkbox when saving. Applying such a preset in Develop computes that photo's own masks;
  clicks are never copied.
- **Grid thumbnails** use masks only if they are already cached, so browsing never starts a model.
- **Keys:** O and M were unused. They are added to the on-screen shortcut help.

## Alternatives
- **A separate full-screen mask editor:** rejected. It takes the photo out of the context of the rest of the edit,
  and Crop mode already shows how a mode on the photo can work inside Develop.
- **Fitting the photo for Refine, as Crop mode does:** rejected after looking at the code. The zoom control could
  report clicks through its own transform, and zoom matters for small, distant players.
- **A coloured tint over the selection:** not chosen. Darkening the outside keeps the selected area's real colours
  visible while its sliders are moved, and it matches what was graded.

## Consequences
- `DevelopViewModel` no longer builds its recipe from the global sliders alone. Before B5 it would have dropped a
  photo's masks on the next save; a test now covers this.
- **New pieces:**
  - `MaskListViewModel` and `LocalAdjustmentViewModel` (kept out of the already large `DevelopViewModel`)
  - `MaskOverlayPainter`
  - a point mode on `ZoomPanImage`
  - `EditRenderer.RenderMask`
  - `IMaskService.TryGetCached`
- **Limits:**
  - Batch apply with masks and export notes for left-out masks are B6.
  - A grid thumbnail rendered before its mask was cached shows the mask only after the recipe next changes.
  - The overlay's edge is the mask after refinement at preview size, so it can differ by a pixel or two from export
    size.
