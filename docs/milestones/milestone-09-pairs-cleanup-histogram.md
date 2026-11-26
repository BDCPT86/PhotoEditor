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
> - **A5 done:** **Settings → Culling → "Show RAW + JPEG pairs as one photo"** (`library.pairRawJpeg`, on by
>   default), with a line saying what it does.
>   - **Off:** each file has its own tile and its own decisions, as in M07.
>   - **Changing it:** the Library rebuilds its tiles when you go back to it. It closes the viewer or compare first, if
>     either was left open. Returning with the setting unchanged doesn't reload.
>   - **Decisions** made while pairs were shown separately are kept. When pairs are shown again, the tile shows the
>     JPEG's decision, as for any pair whose files disagree.
>   - **Tests:** 4 workflow tests.
> - **A6 done (in-app check, 2026-11-26):**
>   - **Setup:** the Debug build, an isolated data folder, and copies of the 12 R50 pairs renamed `IMG_9001`–`IMG_9012`
>     by matching capture times (all 12 matched). Driven through UI Automation.
>   - **Results:**
>     - **Import:** 24 files gave 12 tiles, each marked RAW + JPEG.
>     - **Decisions:** Keep and 3 stars on IMG_9001, then with the setting off the CR3 tile read "Keep, 3 stars". A
>       Reject followed by Ctrl+Z reverted both files ("Undid: marked reject").
>     - **Viewer:** the info line starts "RAW + JPEG · 6000 × 4000 · Canon EOS R50 …".
>     - **Analyze:** 0 similar groups with pairs shown as one photo. With the setting off it found 12 groups (every
>       pair with itself), and with it back on, 0 again.
>     - **Copy files** with `{date:yyyy-MM-dd}_{seq:3}`:
>       - Both files gave 24 (12 names with a CR3 and a JPG, such as `2023-01-24_001.CR3` and `.JPG`).
>       - RAW only gave 12 CR3 files, and JPEG only 12 JPG files.
>       - All 24 copies match an original byte for byte.
>     - **Edited JPEGs:** 12. **Manifest:** 24 rows, 12 of them CR3.
>     - **Panel counts:** "12 photo(s) · 24 files", and "Copied 24 files (12 photos)".
>     - **Originals and log:** the test copies are unchanged, and the log has no errors or warnings.
>   - **Fixed after the check:** the name preview cut off the second name of a pair. It now reads
>     `2023-01-24_001.JPG + .CR3`. This fix is covered by a test but hasn't been seen in the app.
>   - **Changed after the check (your choice):** when a whole shoot was pairs, every tile repeated "RAW+JPEG" in its
>     caption.
>     - **Now:** the mark is an informational mark like ◐ and ▣. It sits on the photo's bottom-right corner on the scrim
>       and shows only while the tile is hovered or selected. It is built only then, so the grid stays light.
>     - **The design system** lists it.
>     - **Seen in the app:** only the selected tile showed it. The caption no longer has it, so file names don't move.
> - **B1 done:** `IRecycleBin` (Application) and `RecycleBinService` over `WindowsRecycleBinPlatform` (Infrastructure).
>   Nothing in the app uses it yet; B2 adds the command.
>   - **Why three layers:** Microsoft's documentation doesn't say what Windows does with a file it can't recycle when
>     told not to ask. So nothing relies on Windows getting it right.
>     1. **Before each file** (checked again just before its move), it's refused unless:
>        - it is on a local fixed drive (drive letter, `DriveType.Fixed`, ready);
>        - Windows has Recycle Bin settings for the drive (`BitBucket\Volume\{guid}`), with `NukeOnDelete` off and the
>          `NoRecycleFiles` policy not set;
>        - it is smaller than the bin's `MaxCapacity`.
>
>        A network share, a removable drive or a SUBST drive is refused.
>     2. **The move:** the shell's file operation, one file at a time, with allow-undo and no confirmation. It also
>        asks for the shell's "will be deleted permanently" warning, which Microsoft documents as overriding "no
>        confirmation" (`FOF_WANTNUKEWARNING`).
>     3. **After each file:** the file must be gone and the drive's Recycle Bin must hold one more item. Otherwise
>        moving stops, and the rest are not tried.
>   - **Results per file:** moved, skipped (with the reason), failed (still there), not confirmed, or not tried. Every
>     file is logged.
>   - **Tests:**
>     - 14 rule tests over a fake Windows layer.
>     - A network path is refused (always runs).
>     - Opt-in with `FRAMEFORGE_TEST_RECYCLE=1`: this PC's drive settings, and a real move of a 1 KB temp file into
>       the Recycle Bin. Run once, with your consent: passed, file gone, bin count up.
>   - **Not verified on real Windows:** whether the permanent-delete warning appears for a file that slipped past the
>     checks (in silent mode). The test through `\\localhost\C$` couldn't run without admin rights. You chose not to
>     use the removable D:. The checks keep such files from reaching Windows, and the count check stops moving if
>     one ever vanishes.
>   - **Known side effects:**
>     - When the Recycle Bin is nearly full, Windows removes its oldest items to make room, as it does for any delete.
>     - The count check can then see no gain and stop moving, which errs on the safe side.
> - **B2 done:** the command and its confirmation panel, shaped with the design skill. Your answers: the button
>   beside the heading, **Delete** opens the panel, and the files that can go are moved while the rest are listed.
>   - **The command:** "Move to Recycle Bin…" sits beside the heading only in the Rejected view when it has photos.
>     **Delete** opens the same panel; elsewhere it says to open the Rejected view, and it does nothing in the viewer
>     or compare.
>   - **What it acts on:** the rejects shown, so a chosen folder or a toolbar filter narrows them. Only photos still
>     marked Reject are included, and both files of each pair.
>   - **The panel** (`RecycleRejectsPanel`, `RecycleRejectsViewModel`) only checks files when it opens:
>     - **The summary:** "3 photos · 6 files · 111 MB".
>     - **From:** each folder, with its file count and full path.
>     - **Skipped:** grouped by reason, with up to three names, and a note that they stay where they are, still marked
>       Reject.
>     - **The note:** how to get a photo back.
>     - **The buttons:** "Move 6 files to Recycle Bin" and Cancel.
>   - **While moving:** progress, and Stop finishes the current file. When nothing can move, the button is off and the
>     panel says why. Opening it moves focus to Cancel, so Esc closes it and a stray Enter can't move anything.
>   - **Afterwards (for now):** the status line says how many files moved. B3 removes the moved photos from the
>     library.
>   - **Seen in the app** on the test copies, with nothing moved:
>     - the button appears only in the Rejected view;
>     - the summary read "3 photos · 6 files · 111 MB", and the confirm button "Move 6 files to Recycle Bin";
>     - Cancel closes the panel, and Delete opens it.
>
>     Esc didn't close it when it was opened from the grid. The fix (focus on Cancel) is built but not yet seen in the
>     app.
>   - **Tests:** 12 workflow tests, with the Recycle Bin faked.
> - **B3 done:** photos whose files went to the Recycle Bin leave the library.
>   - **The removal:** `IPhotoRepository.RemoveAsync` (also on `IPhotoLibraryService`) takes each photo's decision,
>     edit and analysis with it, through the cascading foreign keys of the existing migrations. A database test
>     confirms it, so no migration was needed. Then the library reloads.
>   - **What stays:**
>     - A file that wasn't moved keeps its photo. If a pair's RAW file stayed, it becomes a photo of its own, still
>       marked Reject.
>     - A file that vanished without reaching the Recycle Bin (moving stopped) keeps its photo too, so it isn't lost
>       from view.
>   - **Undo:** forgets only the steps that touched removed photos (`DecisionHistory.Forget`). Other decisions can
>     still be undone.
>   - **Caches:** thumbnails and masks are keyed by hashes and age out under the existing cache limit.
>   - **The result line:** for example "Moved 48 files (31 photos) to the Recycle Bin. 3 not moved (3 no Recycle Bin
>     on the drive). The log lists every file." It's a warning when anything stayed, moving stopped, or Stop was
>     pressed. The log has one line per file (from B1).
>   - **Tests:** 2 database tests, 4 workflow tests and 1 history test.
>   - **Not seen in the app yet,** nor is the B2 Esc fix. A real move of test copies belongs in phase B's in-app
>     check, with your consent, since the files land in your Recycle Bin.
> - **B4 done:** the promise now reads "FrameForge never changes your photos. It moves rejects to the Recycle Bin only
>   when you ask, and only after you confirm."
>   - **Where it changed:**
>     - **About** (in the app).
>     - **README:** the old "never … moved, or deleted" line, which also says files on drives without a Recycle Bin are
>       skipped, never deleted.
>     - **User guide:** the first of its three promises, and the published page (v15).
>     - **The empty library's hint,** which said originals are "never moved".
>   - **Kept as they are:** "never changed" elsewhere stays true, since moving a file to the Recycle Bin doesn't change
>     it.
>   - **Not changed (yours to decide):**
>     - `PRODUCT.md` still says "Originals are sacred: read-only, always".
>     - `CLAUDE.md` says "Keep original image files read-only".
>
>     Both stay true of the files' contents.
>   - **Phase D** adds the full guide sections for pairs and the Recycle Bin.
> - **Phase B in-app check (2026-11-26):**
>   - **Setup:** the Debug build, a new isolated library, and fresh copies of the 24 renamed R50 test files.
>   - **Results:**
>     - 3 pairs were rejected, and the Rejected view showed 3.
>     - **Delete** opened the panel and **Esc** closed it, so the B2 focus fix works.
>     - **The panel** read "3 photos · 6 files · 113 MB" and "Move 6 files to Recycle Bin".
>     - **Pressed:**
>       - in 1.3 s the status read "Moved 6 files (3 photos) to the Recycle Bin. The log lists every file.";
>       - the Rejected view went to 0, and All photos to 9;
>       - the 6 files left the folder (18 remain);
>       - the Recycle Bin went from 1 item to 7.
>     - **The log:** one line per file, and no errors or warnings.
>   - **Tidied after the check:** the per-file log lines now read "Moved to the Recycle Bin: <path>", and a file not
>     moved gets a warning with the reason.
>   - **Your Recycle Bin** holds the 6 test files (`IMG_9001`–`IMG_9003`, .JPG and .CR3) and B1's 1 KB
>     `ff-recycle-test-….tmp`. You can delete them there.
>   - **Not covered:** a skipped file on a real memory card or network drive (only in tests), and the permanent-delete
>     warning (see B1).
>   - **Unexplained (B1):** one full test run reported 1 failure. The runner kept no results file, so I couldn't identify
>     the test. The next 5 full runs passed (933 tests).
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
