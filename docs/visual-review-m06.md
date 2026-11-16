# Milestone 06 — visual review of edits

How the editing output was checked by eye, what was found, and how to repeat it. The photos used are the user's own.
They were rendered locally, and **no photos or rendered images are stored in the repository**.

## Method
`tools/FrameForge.Evaluation review` renders a fixed set of recipes onto a sample of photos and writes one contact
sheet per photo. The sheets are copies at preview size, saved to a folder you choose outside the photos folder.

```
dotnet run --project tools/FrameForge.Evaluation -c Release -- review --photos <folder> --sheets <output folder> --count 6
```

Each sheet has these columns: original · +1 EV · −1 EV · warm +50 · cool −50 · contrast +50 ·
highlights −100 with shadows +100 · saturation −100 · vibrance +80 · straighten 5° with a 4:5 crop.

## Run on 2026-11-16 (6 photos from a 1,010-photo cricket set, Canon JPEGs, 6000×4000 and 4000×6000)

| Check | Result |
|---|---|
| Exposure ±1 EV | Brightens and darkens evenly; hue is kept. At −1 EV, areas already clipped in the JPEG turn flat grey. That is expected: the data is not there. |
| Temperature ±50 | A clear warm/cool shift; neutral greys keep their brightness. |
| Contrast +50 | Punchier, with no clipping of the end points beyond what was already clipped. |
| Highlights −100 + Shadows +100 | **First draft: failed.** On a bright portrait, white turned light grey and the picture looked foggy. **After the fix:** whites keep their texture, the shaded face lifts, and the background keeps its contrast. |
| Saturation −100 | A neutral black-and-white; no colour cast. |
| Vibrance +80 | Lifts muted grass and sky more than the already-saturated cap and stumps. |
| Straighten 5° + 4:5 crop | Level horizon, no blank corners, correct framing on landscape and portrait photos. |
| Orientation | The portrait photo (EXIF-rotated) renders upright in every variant. |

**Fix made:** the shadows/highlights and negative-contrast curves were redesigned so black stays 0 and white stays 1
(ADR 0006). A test now pins that end-point rule, and golden-pixel tests pin the output of every adjustment.

## Speed (development machine, 20 logical CPUs)
- Preview render at 2048 px: median 5 ms, maximum 24 ms (60 renders). Sliders feel immediate.
- Full 24 MP render (tone + colour + straighten, not counting decode): median 245 ms.
- In the review tool, peak working set was about 725 MB, from holding several full-size images at once. The app
  exports one photo at a time.

## In-app check (2026-11-16, isolated data folder, the same 1,010 photos)
- The database migration `AddPhotoEditsAndPresets` applied to the existing test library.
- **D** opened Develop. Sliders updated the preview live, with the UI message loop at most 7.6 ms. **✎ edited** appeared, and
  Undo became available.
- **R** opened crop mode: the frame, thirds grid and handles show over the whole frame. The crop takes keyboard focus:
  arrows moved it and Ctrl+arrows resized it, as read back through UI Automation.
- **Ctrl+Z / Ctrl+Y** while developing undid and redid the crop. **P** still marked the photo Keep.
- Back in the grid: **✎** and ✓ on the tile, and the thumbnail showed the warmer, cropped edit. Edits survived a restart.
- **Export → Edited JPEGs**:
  - output was 6000×3376 (the crop at full resolution), matching the preview
  - camera EXIF kept, orientation reset to 1
  - the original's SHA-256 was unchanged
- **Fixed during the check:**
  - Toggle buttons used the unthemed light style.
  - The aspect and preset dropdowns were unthemed; they are now chips and a dark list.
  - The crop overlay was invisible to UI Automation and didn't take focus, so arrow keys went to another control.
  - The new Presets button crowded out the photo count, which now has its own row.

## Not yet covered
- Banding under extreme pushes on smooth skies was not systematically tested. 8-bit sources limit how far you can
  push them.
- Wide-gamut (Adobe RGB) JPEGs were not in the sample.
- This is a visual check by one reviewer on one set. It does not measure perceived quality.
