# 0020 — RAW files through LibRaw and their embedded previews

**Date:** 2026-11-25
**Status:** Accepted for M07 phase A; A1–A5 are in place, A6 follows (see the M07 brief)

## Context
- FrameForge reads JPEGs only. Windows has no CR3 decoder installed (no WIC codec).
- The M07 RAW spike ([report](../benchmarks/m07-raw-spike.md)) ran LibRaw 0.22.3 on 12 Canon EOS R50 CR3 files:
  - **All 12 decoded,** and no damaged copy crashed the library.
  - **Every file embeds a full-size (6000 × 4000) JPEG** that matches the camera's own JPEG (ΔE 0.1). It takes 2 ms
    to extract, and decodes at JPEG speed.
  - **A full RAW develop takes about 2.5 s and up to 880 MB,** and looks darker and flatter than Canon's rendering.

## Decision

### The library: LibRaw 0.22.3, under the LGPL 2.1
- **What ships:** the unmodified official Windows build (`third_party/libraw/0.22.3/libraw.dll`, with the package's and
  the DLL's SHA-256 in its README), as a separate DLL copied next to the app.
- **The licence:** LibRaw may be used under the LGPL 2.1 or the CDDL 1.0. We use the **LGPL 2.1**, the more widely
  understood of the two.
  - Its licence and copyright texts ship in `licenses\`, and it is listed in the notices.
  - Before FrameForge is given to anyone else, the source archive of this version is shipped with it, or offered in
    writing (licence checklist).
- **It needs the Visual C++ runtime,** which the installer adds when it is missing (ADR 0019).

### Loading and checking it (`FrameForge.Raw`, `LibRawLibrary`)
- **Where it loads from:** the app's own folder only, so another `libraw.dll` on the PATH is never picked up.
- **Fixed offsets:** a few values (orientation, make and model, capture time and exposure, lens) are read from LibRaw's
  structures at offsets taken from the 0.22.3 headers (`RawReader`), which avoids writing a C shim. So:
  - **A different LibRaw version is refused.** RAW is turned off with a message saying which version is needed.
  - **A different build of 0.22.3 is accepted** and noted (its SHA-256 differs from the tested one). This keeps the
    library replaceable, as the LGPL requires; refusing every other file would not.
  - **A missing DLL, or one Windows can't load** (no Visual C++ runtime), turns RAW off with the reason. It never
    crashes the app.
- **Concurrency:** each read opens its own LibRaw handle and closes it, so reads are safe in parallel. Original files are
  only read.

### What is read (`RawReader`)
- **`ReadInfo`:** camera make and model, lens, the size as stored (6000 × 4000 on the R50), the orientation as an EXIF
  value, ISO, shutter, aperture, focal length, and the capture time. LibRaw turns the camera's clock time into Unix
  time with this PC's zone, and FrameForge turns it back.
  - LibRaw's `flip` maps to EXIF orientation: 0 → 1, 3 → 3 (180°), 5 → 8 (90° anticlockwise), 6 → 6 (90° clockwise).
    This is the same mapping the spike's comparisons confirmed.
- **`ReadPreview`:** the embedded JPEG's bytes and the same orientation.
  - A file without a JPEG preview is a read error, for now.

### Import and details (A2)
- **Which files import:** `.CR3` is a supported format (`SupportedPhotoFormats.IsRaw`). Other RAW formats stay off until
  they are tested.
- **How details are read:** `IPhotoMetadataReader` is now `PhotoMetadataReader`, which sends RAW files to
  `RawPhotoMetadataReader` (LibRaw) and everything else to WIC as before.
  - A RAW file LibRaw can't read, or RAW being unavailable on this computer, becomes `PhotoReadException`. Import lists
    the file as **Unreadable**, with the reason.
- **Details stored like a JPEG's:**
  - the size as stored and the EXIF orientation
  - the exposure written "1/1000" or "2.5"
  - the camera model in Canon's EXIF form ("Canon EOS R50", not LibRaw's "EOS R50"), so a RAW file and its JPEG name
    the camera the same way
- **Checked on the 12 R50 samples:** each RAW file's capture time equals that of one of the camera's JPEGs. LibRaw's
  conversion to this PC's zone and back loses nothing.

### Showing, analysing and editing (A3)
- **One choke point:** every pixel decode goes through `WicDecoding.DecodeOriented`, which serves thumbnails, the viewer
  (screen size, look-ahead and full resolution), analysis, AI, masks and edit rendering.
- **For a RAW file,** it takes the embedded JPEG from LibRaw (`RawPreviews`), decodes it from memory, and rotates it by
  the RAW file's orientation. Nothing downstream knows the difference.
- **An unreadable RAW file** (or RAW unavailable here) is a `PhotoReadException`, so its tile says "Can't display" like a
  damaged JPEG.
- **Checked on the R50 samples:**
  - all 12 decode upright at 320 px (thumbnail), 1024 px (analysis) and 2048 px (screen); portrait files come out
    taller than wide
  - full size is 6000 × 4000
  - the mean difference from the paired camera JPEG is under 6 of 255 per channel at 256 px; the spike measured
    ΔE 0.1
- **What this means for RAW files:** editing works on the camera's 8-bit rendering. Blown highlights in the preview stay
  blown, exactly as with the camera's JPEG. The latitude the sensor data holds needs the RAW develop of a later
  milestone.

### Exporting edited JPEGs (A4)
- **Originals** export as the CR3 file itself, copied unchanged, as for any photo.
- **Edited JPEGs of a RAW file** get their EXIF from the RAW file's header (`RawExif`), not from the embedded JPEG.
  - **Why not copy the embedded JPEG's EXIF:** LibRaw gives that JPEG a small block of its own, which says "dcraw v9.26"
    as the software and "EOS R50" as the model, and has no lens or original capture time.
  - **Written:** make, model the EXIF way ("Canon EOS R50"), orientation 1, capture time (original, digitised and file
    time), exposure, f-number, ISO, focal length and lens. With the metadata choice "None", nothing is written, as for a
    JPEG.
  - **Not written:** GPS, copyright, owner and the other EXIF, XMP and IPTC a camera JPEG carries, which LibRaw doesn't
    give. So "All" and "All except location" give the same result for a RAW file, with no location. The colour space is
    left out, because the embedded JPEG doesn't state it.
  - **Rating and label** are written as for a JPEG (ADR 0017).
  - **A RAW file whose details can't be read** still exports. Its JPEG gets only the rating and label, and the export
    counts it as "without metadata".
- **Checked on the 12 R50 samples (test):** each edited JPEG's make, model, lens, capture time, ISO, exposure, f-number
  and focal length equal what import read from the RAW file. Its model, ISO and exposure equal the camera's own JPEG
  of the same moment, and the RAW file is unchanged.

### Checked in the app (A5, 2026-11-26)
Debug build, on copies of the 12 R50 CR3 files and their 12 camera JPEGs, in an isolated data folder, driven by UI
Automation:
- **Import:** 24 new photos in 4.3 s, none unreadable, no warnings or errors in the log.
- **Grid:** every RAW tile shows its thumbnail upright and matches its JPEG's; none says "Can't display".
- **Viewer:** RAW files show upright with their details (size, camera, lens, ISO, exposure, aperture, focal length,
  time).
  - Stepping through all 24 photos takes a median 317 ms per photo with no pause, and 380 ms after a 1 s look.
  - The same script gives 331 ms on the JPEG-only test library. Most of it is the script searching the automation tree
    (about 0.3 s per search, see the [performance report](../benchmarks/m07-performance.md)). So RAW files take at most
    about 50 ms more than JPEGs by this measure; the app's own timing was not repeated.
- **Analyze:** 24 analysed in about 1.3 s. Each RAW file and its JPEG form a similar group (12 groups), with the usual
  marks.
- **Export:** all 24 as full-size edited JPEGs (Full-size JPEG profile) in 10.2 s.
  - Each RAW file's export has the same upright size and the same model, lens, capture time, ISO, exposure and f-number
    as its camera JPEG's export.
- **Found and fixed during the check:** the earlier fix for the view model's name behind grid tiles also hid the
  tiles' analysis marks. Those tile parts now get an empty template by default instead of being collapsed. The check
  found the marks back and no view model text (the grid before either fix showed 48 such elements on these 24 tiles).

### Plan for the rest of phase A (approved)
- **Previews:** every RAW file is shown, analysed and edited through its embedded JPEG. A true RAW develop waits for a
  later milestone.
- **RAW + JPEG pairs:** imported as separate photos.
- **Formats:** CR3 only, the one format tested.

## Tests
- **Unit tests:** the bundled library loads as the tested build; a missing file and a non-DLL each turn RAW off with a
  reason; the flip-to-orientation mapping; a non-RAW file is a read error and a missing file is "not found".
- **On real files** (opt-in, when `FRAMEFORGE_TEST_RAW` names the user's R50 folder; the files are never in the
  repository):
  - make, model, size, orientation, time, ISO and shutter are read
  - the preview is a full JPEG of more than 500 KB
  - a zeroed header and a cut-off copy are read errors, not crashes
- **Run on 2026-11-25:** 10 of 10 passed, none skipped.

## Consequences
- **Upgrading LibRaw** means rechecking the offsets against the new headers and updating `TestedVersion` and
  `TestedSha256`.
- **LibRaw runs inside the FrameForge process,** so a native crash on some file would take the app down. No damaged file
  did in the spike or the tests, and the crash report (ADR 0019) would record one.
- **The test tool** (`tools/FrameForge.Evaluation`) keeps its own wrapper, which also develops sensor data for the
  spike. The app's wrapper has only what the app needs.
