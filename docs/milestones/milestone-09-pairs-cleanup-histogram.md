# Milestone 09 — RAW + JPEG pairs, clearing out rejects, histogram

> **Status (2026-11-26):** Approved with the recommended answers: the JPEG is the cover of a pair; moved photos leave
> the library; files on drives without a Recycle Bin are refused; H and J for the histogram and overlay.
>
> - **A1 done:** `PhotoPairing` (Core) finds RAW + JPEG pairs by the rule below, with 14 unit tests. It finds 5,000
>   pairs among 10,000 photos in under 100 ms (test; the best of five runs, since other test projects run at the
>   same time).
> - **A2 done:** the library builds one tile per pair (the JPEG). The RAW file is the tile's hidden partner and holds
>   its own decision.
>   - **The tile:** a quiet **RAW+JPEG** word in the caption, with both file names in its tooltip. The viewer's info
>     line starts "RAW + JPEG".
>   - **Counts, filters and sorting** see one photo per pair.
>   - **Decisions** reach both files as one undo step. The status line counts a pair once.
>   - **Files that disagree:** the tile shows the JPEG's decision. The next change writes the result to both, even
>     when the JPEG is already in that state. Undo gives the RAW file its own earlier decision back.
>   - **Tests:** 7 workflow tests and 3 planning tests.
> - **A3 done, with no change to the app:** only the library loads photos, and every other part (Analyze, AI, Develop,
>   Auto, presets, masks, compare) gets the tiles from it. Since A2 those hold the cover only. 4 tests confirm it:
>   - Analyze and AI are given the covers.
>   - A pair with matching hashes isn't grouped as similar.
>   - A preset reaches the cover and not its RAW file.
>   - An edit stored for a RAW file before this milestone stays stored but isn't shown.
> - **A4 done:** each exported file knows its pair's cover (`ExportItem.Cover`), and `ExportFrames` holds the rules.
>   - **Copy files:** a **RAW + JPEG** drop-down (Both files, RAW only, JPEG only) under "Export as". It shows only
>     when the photos in scope include a pair. Photos on their own are always copied. Profiles keep the choice; older
>     stored profiles mean Both.
>   - **Names:**
>     - **With a pattern,** both files are named from the JPEG, with one `{seq}` number per photo
>       (`2026-11-14_002.JPG` and `2026-11-14_002.CR3`).
>     - **With no pattern,** each keeps its own name.
>     - **A clash** renames the pair together (`… (2).JPG` and `… (2).CR3`) with the first number free for both.
>   - **Edited JPEGs:** one per pair, from the JPEG.
>   - **The manifest:** lists both files.
>   - **Counts:** the panel counts photos, plus files when a pair gives two ("2 photo(s) · 3 files"). The result line
>     says "Copied 3 files (2 photos)". The name preview shows both names of a pair.
>   - **Tests:** 9 rule tests, 1 copier test with real files, and 8 panel tests.
>   - **An edit made to a RAW file before this milestone** isn't shown, since the tile is the JPEG.

## Goal
Make culling a real shoot fast from card to keepers. A camera that saves RAW and JPEG should give one photo to decide
on, not two. Rejects should be easy to clear away safely. Editing exposure should show what is clipped.

## Why these three
- **Pairs:** since M07, a Canon R50 shooting RAW+JPEG puts every frame in the grid twice. That doubles the key presses
  and the Analyze work. A decision on one file doesn't reach the other, and Analyze groups each pair as "similar".
- **Rejects:** culling ends with getting rid of the rejects. Today that means finding them by hand in File Explorer.
- **Histogram:** the app measures clipping for analysis but never shows it. A histogram and clipping overlay are basic
  tools for exposure edits.

## Scope (in order)

### Phase A — RAW + JPEG pairs
- **A1. The pairing rule** (in memory, recomputed at load, like similar groups in ADR 0004; no database migration):
  - **Which files pair:** a RAW file and a JPEG in the same folder, with the same name apart from the extension
    (`IMG_0603.CR3` + `IMG_0603.JPG`, any case).
  - **Capture time:** when both files have one, they must match to the second. This guards against the camera's
    counter reset to the same number on another day.
  - **Never paired:** a name with two JPEGs or two RAW files, or a file that is Missing or Unreadable while its partner
    is fine. Each is shown on its own.
  - **Pure domain logic** in Core (`PhotoPairing`), with unit tests.
- **A2. One tile per pair:**
  - **The tile:** shows the JPEG, with a small **RAW+JPEG** mark. The viewer's info line says "RAW + JPEG".
    - The JPEG is the cover because the pixels are the same (the RAW file's preview is the camera's JPEG). The JPEG
      also carries all the camera's metadata (GPS, copyright), which an edited export then keeps.
    - If the JPEG is missing, the RAW file is shown instead.
  - **Counts, filters and sorting** count a pair once.
  - **A decision (Keep/Review/Reject, rating, label) applies to both files**, as one undo step.
  - **Files that already disagree:** when the two files have different decisions (from before this milestone), the
    tile shows the JPEG's decision. The next change writes it to both.
- **A3. Analysis, edits and compare work on the cover only:**
  - Analyze skips the partner, so pairs are no longer "similar" to themselves and the work halves.
  - Edits, masks and Auto apply to the cover.
- **A4. Export:**
  - **Copy files** gets a choice: **RAW and JPEG** (default), **RAW only** or **JPEG only**. Both files of a pair get
    the same name from a pattern (`2026-11-14_001.CR3` and `2026-11-14_001.JPG`).
  - **Edited JPEGs:** one per pair, from the cover.
  - **The manifest:** lists both files.
- **A5. A setting, "Show RAW + JPEG pairs as one photo"** (on by default). Off shows them separately, as in M07. It
  changes only the display; nothing is stored.
- **A6. In-app check:**
  - **Test files:** copies of the 12 R50 RAW/JPEG pairs. The sample files have unrelated names, so the copies are
    renamed by matching capture times.
  - **What is checked:** 12 tiles, a decision reaching both files, undo, Analyze no longer grouping pairs, and each
    export choice.

### Phase B — Moving rejects to the Recycle Bin
- **B1. The service:** `IRecycleBin` in Infrastructure, using Windows' file operation with "allow undo", one file at a
  time so each result is known.
  - **Recycle Bin only:** a drive without a Recycle Bin (network shares, SD cards and most USB drives) would make
    Windows delete the file permanently instead. The files on such drives are **refused**, never deleted.
- **B2. The command, "Move rejects to Recycle Bin…":** in the **Rejected** view. It acts on the rejects shown, so
  choosing a folder narrows it, and it includes the partner file of a pair.
  - **A confirmation panel** shows:
    - how many photos and files, and their total size
    - the folders they are in
    - any files that will be skipped, and why (on a drive without a Recycle Bin, offline, missing)
  - **Nothing happens until it is confirmed.**
- **B3. Afterwards:**
  - **The library:** moved photos are removed from it, with their decisions, edits and analysis. Thumbnails age out of
    the cache.
  - **The result line:** "Moved 48 files (31 photos) to the Recycle Bin; 3 skipped (SD card)." The log lists every
    file.
  - **Getting a file back:** restore it from the Recycle Bin and import the folder again. It returns as a new photo,
    so its Reject mark is gone. The app has no undo for this.
- **B4. The promise changes,** and the user guide, README and About say so: "FrameForge never changes your photos. It
  moves rejects to the Recycle Bin only when you ask, and only after you confirm."

### Phase C — Histogram and clipping overlay
- **C1. The histogram:**
  - **What it shows:** red, green, blue and luminance in 256 bins.
  - **What it is computed from:** the image on screen, so in Develop it follows the edit as you drag. The work runs
    off the UI thread, and quick changes in a row are merged into one update.
  - **Where:**
    - at the top of the Develop panel
    - as a small panel in the viewer, toggled with **H** and remembered
- **C2. The clipping overlay:** **J** toggles it, in the viewer and Develop.
  - **What it marks:** pixels at 255 in any channel are tinted as highlights, and pixels at 0 in every channel as
    shadows.
  - **It is display only,** never part of an export.
  - **The histogram** shows the share of clipped pixels at each end.
- **C3. Keys:** H and J are free today. Both are listed in F1 and in the user guide.

### Phase D — Documents and completion review
- **Documents:**
  - an ADR for pairing and for the Recycle Bin
  - updates to the database schema notes (no change expected)
  - the user guide and its published page
  - the README and the roadmap (as Phase 9)
- **A completion review** against the criteria below.

## Acceptance criteria
- **Pairs:**
  - a RAW+JPEG shoot shows one tile per frame
  - one key press decides both files, and one undo reverts both
  - counts and filters count frames
  - Analyze doesn't group a pair with itself
- **Export:**
  - the export choices produce exactly the files chosen
  - names from a pattern match within a pair
  - originals are never changed
- **Recycle Bin:**
  - files go only to the Recycle Bin
  - files on a drive without one are skipped and listed, never deleted
  - nothing moves before confirmation
  - the library and counts are right afterwards
- **Histogram:** it matches the image on screen (checked against a computed reference in tests), and the overlay never
  appears in an export.
- **Performance:**
  - pairing 10,000 photos adds less than 100 ms to loading the library
  - the histogram updates within 100 ms of an edit at screen size
- **Build and tests:** the build is clean, tests pass, and the in-app checks are documented.

## Risks
- **Permanent deletion by mistake:** this is the main risk of phase B.
  - **Mitigations:**
    - a drive check before any move, allowing fixed drives only, and only those whose Recycle Bin is turned on
    - one file at a time
    - a confirmation listing everything
    - tests with a fake service
  - **The in-app check** uses copies on a fixed drive only, never the user's photos.
- **Pairing mistakes:** the same name on different days in one folder. Matching capture times guards against it, and
  the setting splits pairs if needed.
- **Decisions that already differ** within a pair (only possible for libraries imported since M07): the JPEG's wins on
  display and both are written on the next change.
- **Size of phase A:** decisions, filters, analysis, export naming and the tile all change. It is done in small steps,
  each built and tested.

## Out of scope
- Pairing other combinations (RAW+HEIC, two RAW formats) and sidecars (XMP).
- Deleting files permanently, or emptying the Recycle Bin.
- An in-app undo for moving files to the Recycle Bin.
- A RAW develop; the histogram shows the embedded JPEG's tones for RAW files.

## Questions for approval
1. **Cover photo:** show the **JPEG** for a pair (recommended: same pixels, full metadata), or the RAW file?
2. **After moving to the Recycle Bin:** **remove the photos from the library** (recommended), or keep them as Missing
   with their decisions?
3. **Drives without a Recycle Bin** (SD cards, network): **refuse those files** (recommended), or offer to delete them
   permanently after a second confirmation?
4. **Keys:** **H** for the histogram and **J** for the clipping overlay?
