# 0013 — Presets with masks on many photos

**Date:** 2026-11-23
**Status:** Accepted (Milestone 08 phase B, step B6). The defaults below were chosen by the user.

## Context
Since B2 a preset can include a **Masks** group, and since B5 Develop can save one. Applying such a preset to a grid
selection only rewrote each photo's recipe. That was instant, but the masks were worked out later, wherever the photo
was next drawn (Develop, thumbnails, export). Nobody was told when a photo had no subject, and the slow part was
scattered. The brief asks for masks to be computed per photo during the batch, with progress, cancel and undo, and for
photos that can't be handled to be listed.

## Decision
- **Two paths:**
  - A preset with masks that have settings (`MaskBatch.HasMasks`) runs photo by photo through the same per-photo batch
    as Auto: apply the preset, then work out each of its masks (computed and cached).
  - Every other preset keeps the fast path, unchanged.
- **A mask that can't be made** (no people, no sky) is left off that photo, and the rest of the preset still applies.
  The photo is listed with the reason ("IMG_0605.JPG: Sky left out: No sky found…"), in the same list Auto batch uses.
- **Missing subject models:** a preset with a Subject or Background mask is refused before anything runs, with the
  reason. Without the models every photo would come out without its main mask.
- **Masks without settings** are kept but not worked out until they get settings, so they cost nothing.
- **Clicks are never copied** (unchanged from B2).
- **Source:** a kept mask records the source that made it, so its cached mask is found again by the same key.
- **Time:**
  - **Before starting:** the panel shows an estimate when the preset has masks, using 2–5 s per photo for a subject
    and 0.3–1 s for sky (ADR 0008 and the B5 check).
  - **While running:** it shows "37 of 200 · about 9 min left", from the speed measured so far.
  - **No confirmation dialog:** Cancel keeps what's done, and **Undo last apply** restores it all.
- **Export:** edited-JPEG export loads each photo's masks first (computing any that are missing), so it can report
  every mask it left out. The JPEG is still written with the rest of the edit. The result lists them ("1 saved without a
  mask: …") and is shown as a warning. Before B6 this was only logged.

## Alternatives
- **Skip the whole photo when one of its masks fails:** not chosen. It throws away the parts of the preset that do
  apply, and the photo would look untouched for an unclear reason.
- **Apply without the subject masks when the models are missing:** not chosen. Applying "lift the subject" with no way
  to find one silently does half the job on every photo.
- **Keep the batch instant and work masks out in the background:** rejected. Problems would surface later, one photo
  at a time, and undo would not cover the work.

## Consequences
- **Speed:** a preset with a subject mask on 200 photos takes roughly 7–17 minutes on this laptop's CPU. B7 measures
  it properly.
- **Export:** an export of photos whose masks are not cached yet is slow for the same reason; it shows progress per
  photo as before.
- **Tests:** the per-photo step, the panel path (refusal, notes, undo, estimate, countdown) and the export summary.
  Not checked in the app yet.
