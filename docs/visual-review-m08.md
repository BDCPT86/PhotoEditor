# Visual review — Milestone 08 phase A (Auto tools)

**Date:** 2026-11-17. **Set:** the user's sports photos (1,010 JPEGs, Canon EOS R50), imported into an isolated test
data folder (`FRAMEFORGE_DATA_DIR`), with AI faces analysed at 1024 px. The photos are not in the repository.

These are observations on a handful of photos, not a measured evaluation.

## What was checked in the app
| Tool | Observation | Change made |
|---|---|---|
| Auto light | The first version suggested about −1.5 EV on a correctly exposed sunny scene (white wall, bright grass). It read "bright" as "overexposed". | It now darkens only when more than 1 % is clipped, and then by at most 0.3 EV; otherwise it reports "exposure left as shot". There is a test for this. |
| Auto light | On an already edited photo (+0.6 EV), Natural suggested −0.30 EV, highlights −35 and temperature +9, with the reason shown. | None. The reason matched what the sliders showed. |
| Auto level | On a photo with a wall/grass line, it suggested +0.5°, and the guide sat on the wall line. | None. |
| Auto level | On a synthetic level edge, the first version reported 0.2° because of tied votes. | Ties now resolve to the middle of the peak. |
| Auto crop | Tight, Medium and Wide framed the group of players and kept every face; spectators were ignored. | None. |
| Tray | The first version covered the bottom of the photo. | Moved to its own row below the photo. |
| Develop panel | A dotted focus box showed on the Auto section header. | Section headers are no longer focus stops. |
| Keyboard | After Develop closed, focus fell out of the page, so F (zoom to faces) did nothing until a click. | Focus returns to the photo when Develop or the tray closes. F / Shift+F then stepped "Face 1 of 2" → "Face 2 of 2" → "Face 1 of 2" at 100%. |
| Batch Auto | 12 photos, Light (Natural) + Level + Crop (Medium, 4:5): done in about 4 s. All 12 changed; the 2 with no main face were listed as "not cropped (no main face)". Thumbnails showed the 4:5 crops. | None. |
| Presets panel | On a 1200 px tall window the panel was cut off below the crop ratios: **Apply Auto to selected**, the notes and the result could not be reached. | The panel body now scrolls under a fixed header. |
| Auto-applied filter | It showed exactly the 12 changed photos. After **Undo last apply**, they stayed in the filtered view. | The view now refreshes after an Auto run or an undo (empty after undo, tested). |
| Result text | It said the skipped photos were "listed below", but the list is above it. | Now "listed above". |

## Not yet reviewed
- Auto light across a wide range of photos (night, indoor, backlit). No blind A/B has been done yet.
- Auto crop agreement with the user's own crops on a labelled sample.

## Polish pass (after the batch check)
| Finding | Change made |
|---|---|
| Side panels (Presets, Export, analysis details, shortcuts) covered the right end of the filter rows, including the Auto-applied chip, and in the viewer they covered the photo. | The panels now sit in their own column, so the grid or viewer narrows beside them. Checked by eye in the grid; the viewer uses the same layout but hasn't been checked. |
| With the grid narrowed, the toolbar clipped: **Compare (C)** disappeared and "Presets…" was cut. | New `SplitToolbarPanel`: one row when it fits, otherwise the action buttons drop to a second, right-aligned row. |
| The ANALYSIS chips were wider than the narrowed grid and clipped. | Each filter group's chips wrap, aligned under the first chip rather than under the label. |
| A run where some photos had a step skipped showed as a red warning. | Skipped steps are information, not failures: the result is a normal ✓ ("2 had a step skipped"), and only a cancel is a warning. |
