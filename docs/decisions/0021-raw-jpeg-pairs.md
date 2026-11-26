# 0021 — RAW + JPEG pairs shown as one photo

**Date:** 2026-11-26
**Status:** Accepted (Milestone 09, phase A)

## Context
Since M07, a camera that saves RAW and JPEG puts every frame in the grid twice. That doubled the key presses and the
Analyze work. A decision on one file didn't reach the other, and Analyze grouped each pair as "similar". The RAW file's
pixels in FrameForge are its embedded JPEG (ADR 0020), so the two files of a pair look the same.

## Decision
- **The rule** (`PhotoPairing` in Core): a RAW file and a JPEG pair when all of these hold:
  - They're in the same folder and have the same name apart from the extension, ignoring case.
  - No other JPEG or RAW file has that name.
  - Both were read; a Missing or Unreadable file is shown on its own.
  - Their capture times match to the second, when both have one. This keeps apart files that share a name only because
    the camera's counter came round again.
- **Worked out in memory at every load,** like similar groups (ADR 0004). Nothing is stored, so there is no migration
  and nothing goes stale. It takes under 100 ms for 10,000 photos (test).
- **The JPEG is the cover:** same pixels, and it carries all of the camera's metadata.
  - **The tile and the RAW file:** the grid has one tile per pair. The RAW file is the tile's hidden `Partner`, a tile
    object of its own that holds its decision.
  - **The mark:** a RAW+JPEG mark on the photo's corner on hover or selection, with "RAW + JPEG" in the viewer's info
    line.
- **Decisions reach both files as one undo step** (`DecisionService.Plan` with partners). When the two files disagree
  (possible only for libraries imported since M07), the tile shows the JPEG's decision. The next change writes the
  result to both, and undo gives each file its own earlier decision back.
- **Analysis, AI, edits, presets, Auto, masks and compare** see only the cover, because they all get their photos from
  the grid. A pair is never "similar" to itself, and the work halves.
- **Export** (`ExportItem.Cover`, `ExportFrames`):
  - **Copy files** offers both files, RAW only or JPEG only. Profiles keep the choice; profiles stored before this
    mean both.
  - **Names:** with a pattern, both files are named from the cover with one `{seq}` number per photo. A clash renames
    the pair together (`ExportNamePlanner.PlanFrames`).
  - **Edited JPEGs** are made once per pair, from the cover. The manifest lists both files.
- **A setting,** "Show RAW + JPEG pairs as one photo" (`library.pairRawJpeg`, on by default), turns it off. It changes
  only the display; the library rebuilds its tiles when you go back to it.

## Alternatives
- **The RAW file as the cover:** rejected. Its pixels are the same embedded JPEG, and it lacks some of the camera's
  metadata (GPS, copyright), which an edited export would then lose.
- **Storing pairs in the database:** rejected. It needs a migration, and it would go stale when files are renamed or
  removed outside FrameForge.
- **Pairing by name alone:** rejected. A counter that wraps round would pair photos from different days.
- **Pairing other combinations** (RAW + HEIC, two RAW formats) and sidecars (XMP): out of scope.

## Consequences
- A RAW + JPEG shoot culls at the speed of a JPEG-only one, with one decision per frame on both files.
- An edit made to a RAW file before M09 stays stored but isn't shown while pairs are on, since the tile is the JPEG.
- Turning the setting off shows each file with its own decisions again; nothing is lost either way.
