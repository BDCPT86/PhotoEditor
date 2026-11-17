# 0007 — Auto light, Auto level, Auto crop and zoom to faces (no new model)

**Date:** 2026-11-17
**Status:** Accepted (Milestone 08, phase A)

## Context
Milestone 08 starts with the parts that need no new model: Auto settings, auto-straighten, auto-crop from the M05 face
boxes, and zoom to faces. The user asked that Auto *offer a few options to choose from* rather than apply one answer,
and that it work equally for one photo (Develop) and for many (batch).

## Decision

### Suggestions, never silent edits
Every Auto tool is a pure function in `FrameForge.Application/Editing/Auto` that returns `AutoSuggestions`:
- up to three named options, each a full `EditRecipe` plus a plain-language reason
- or, when it cannot help, no options and a reason ("Already level", "No faces found…")

Develop shows the options in a tray below the photo:
- each option has a rendered preview, and the focused option previews on the main photo
- ←/→ or 1–3 preview an option, Enter or **Apply** accepts it, and Esc dismisses the tray
- nothing changes until the user accepts, and accepting is one undo step (the M06 undo chain)

### Auto light (`AutoTone`)
Measures the photo at preview size: median linear luminance, the 99th percentile of the brightest channel, the share
of clipped pixels, the dark share, and the colour of near-neutral pixels.
- **Natural:** exposure aims the mid-tones at a fixed target, but **only ever brightens**. A bright scene is left as
  shot. Brightening is held back to keep highlight headroom.
- **Highlights and shadows:** highlights are pulled down only when at least 0.5 % is clipped (in proportion, at most
  −40). Shadows are lifted in proportion to the dark share.
- **Temperature:** moved toward neutral using the neutral pixels (at least 2 % of the photo must be neutral), only
  when the correction would be at least ±25, and limited to ±30. Milder casts are left as shot.
- **Retune (2026-11-17):** the first version also darkened by up to 0.3 EV when more than 1 % was clipped, and applied
  any temperature and highlight change. In the blind A/B ([evaluation-m08.md](../evaluation-m08.md)) the user chose the
  original in 14 of 16 decided pairs. Every darkened photo lost, mild warming never won, and only the shadow lift was
  ever preferred. Natural now leaves most photos unchanged.
- **Brighter** (+0.4 EV) and **Punchy** (contrast and vibrance) are variations on Natural.
- Only the light and colour fields change; crop, straighten and saturation are kept.

### Auto level (`AutoStraighten`)
- Sobel edges at 640 px, then a Hough vote over ±15° around horizontal and vertical, in 0.1° steps.
- A line counts only if it spans at least 25 % of the frame **and** stands out from the background vote by 6×. This
  stops noise and texture from producing a "line".
- Options are the main line, the second line if one exists, and **Keep as is**. A tilt below 0.15° reports
  "Already level".
- The focused option draws a guide over the line it found, so the user sees *why*.

### Auto crop (`AutoCrop`)
Uses the stored M05 face boxes through `IFaceLookup`, and only the main faces (the M05 rule), so spectators are
ignored.
- **Options:** Tight, Medium and Wide, at the ratio chosen under Crop, or the photo's own ratio. They are 12, 20 and
  30 face heights tall (whole figure, figure with room around it, figure in its setting). A standing body is about
  7–8 face heights, so even Tight usually shows the whole figure.
- **Framing:** the eye line sits at 28 % of the crop height from the top (a little above the upper third). A single
  subject goes on the nearer vertical third; a group is centred.
- **Limits:**
  - a crop never cuts a face
  - a crop never goes below a 2,000 px long side (or the photo's own size, if smaller)
  - options that come out the same are dropped
  - an option covering at least 95 % of the frame both ways isn't offered; if none is left, Auto says the photo is
    already framed loosely, and batch Auto lists it as "not cropped (already framed loosely)"
- **Retune (2026-11-17):** the first version used 3.5, 7 and 12 face heights with the eyes on the upper third. The
  user's own crops ([evaluation-m08.md](../evaluation-m08.md)) were all looser than even Auto's closest option (median
  2.7× the area), about 21 face heights tall with the eyes at 28 %, and kept the whole frame on 13 of 30 photos. The
  numbers above come from those crops.
- Crops are computed through `EditGeometry`, including its new inverse `ToOutput`, so they respect quarter turns and
  straighten.

### Batch (`AutoBatch`)
The Presets panel can run Light (with a chosen variant), Level and Crop (variant and ratio) over the selection, per
photo, in the order light → level → crop. This uses the M06 batch rules: progress, cancel, saves every 25 photos, and
**Undo last apply**. Photos that a step could not help are listed with the reason. Tiles changed by Auto are marked
for the **Auto-applied** filter chip for the rest of the session; the mark is not persisted.

### Zoom to faces
In the viewer, **F** / **Shift+F** step the 100 % view through the counted faces. Faces are mapped through the
photo's edit geometry, and faces that are cropped away are skipped. This is off while Develop is open, because F
would clash with editing.

## Alternatives
- **Applying one Auto result directly:** rejected, because the user asked to choose between options. One-click Auto
  is still available: accept the first option with Enter.
- **A learned auto-enhance model:** deferred. The brief requires a model to beat this deterministic baseline on a
  human-reviewed set first.
- **OpenCV for edges and Hough:** not needed. The managed version is about 240 lines, deterministic and tested.
  Native libraries would add deployment surface.
- **Persisting the Auto-applied mark:** deferred. It would need a schema change, and its value beyond a session is
  unproven.

## Consequences
- Every tool works with no model installed. Auto crop needs face data from a previous Analyze with faces on, and
  says so when it has none.
- **Auto light is a starting point, not a grade.** It is tuned for correctly exposed 8-bit JPEGs. It does not recover
  blown highlights, and its targets are heuristics that have not yet been checked on a human-reviewed set.
- Auto level finds dominant straight lines. Scenes whose strongest line is not meant to be level (a slope, a
  diagonal fence) can give a wrong suggestion, so **Keep as is** is always offered.
- Auto crop frames faces only. A subject seen from behind, or the ball, isn't considered until subject masks
  (phase B) exist.
