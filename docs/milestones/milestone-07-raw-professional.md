# Milestone 07 — RAW and Professional Workflow

> **Status (2026-11-26):** All phases (A–F) are implemented, each approved on its own. Milestone 08 was done first, because it
> depends only on M05 and M06.
>
> - **Phase A (RAW, Canon CR3), implemented.** See [ADR 0020](../decisions/0020-raw-through-libraw-previews.md).
>   - A0: the installer adds the Visual C++ runtime when it is missing, and the app turns AI off instead of crashing
>     when the runtime can't load (ADR 0019).
>   - A1: LibRaw 0.22.3 (LGPL-2.1) is vendored unmodified and wrapped in `FrameForge.Raw`; other versions are refused.
>   - A2: `.CR3` files import with their details (camera, lens, exposure, capture time, orientation).
>   - A3: RAW files show, analyse and edit through their embedded full-size JPEG; RAW and JPEG import as separate
>     photos.
>   - A4: edited JPEGs of RAW files carry the camera details read from the RAW file (no GPS).
>   - A5: checked in the app on copies of 12 R50 CR3 files and their 12 camera JPEGs: import, grid, viewer, Analyze
>     and export.
>   - A6: the user guide lists the supported format and its limits.
>   - **Supported:** Canon CR3, tested with the Canon EOS R50 only. No other RAW format is imported, and no sensor-data
>     develop yet.
>   - The spike before it:
>     - The spike tool is `raw-spike` in `tools/FrameForge.Evaluation`, using LibRaw 0.22.3, the first version that lists
>       the Canon EOS R50. Sdcb.LibRaw was dropped: its native package is LibRaw 0.21.1, with no R50 support and from
>       before the 0.22.1 security fixes.
>     - On 12 R50 CR3 files with their camera JPEGs, all decoded and no damaged copy crashed LibRaw. The embedded full-size
>       preview matches the camera JPEG. A full develop takes about 2.5 s and up to about 880 MB, and looks darker and
>       flatter than the camera's. See the [compatibility report](../benchmarks/m07-raw-spike.md).
> - **Phase B (export naming, metadata, profiles), implemented.** File-name patterns; a metadata choice for edited JPEGs
>   (all, all except location, none) with the rating and label written in; saved export profiles and the last export's
>   settings. It also fixed edited exports of photos with an embedded colour profile, which were labelled with the wrong
>   profile. See [ADR 0017](../decisions/0017-export-naming-and-metadata.md).
>
>   Checked in the app on 2026-11-25, on copies of six R50 JPEGs with GPS, in an isolated data folder:
>   - The Web profile plus a `{date}_{seq}_{name}` pattern, saved as a profile of its own, exported 3 kept photos as
>     `2026-11-14_001_IMG_0603.jpg` and so on.
>   - The exports were 2048 × 1365, without GPS, keeping the camera model and capture time, and without a rating.
>   - After a restart the panel reopened on the saved profile and pattern.
>   - The check found that drop-downs bound to objects showed the object's raw text when closed. The shared ComboBox
>     template now honours `DisplayMemberPath`.
> - **Phase C (moved folders, offline drives), implemented.**
>   - Photos on a drive or folder that can't be reached show as Offline, with the reason, without a database change.
>   - An import that finds nothing never marks a folder Missing.
>   - **Locate folder…** points a moved folder's photos at the new place, in one transaction, keeping decisions and
>     edits.
>
>   See [ADR 0016](../decisions/0016-moved-folders-and-offline-drives.md).
> - **Phase D (performance and memory), implemented.**
>   - The viewer shows photos at screen size and loads the next one ahead, so stepping through them is instant. Full
>     resolution loads only when zooming in.
>   - Memory is given back after the viewer and after exports.
>   - Grid view and sort changes are faster, and the disk caches are kept under 2 GB.
>
>   See the [performance report](../benchmarks/m07-performance.md) for the baseline, the changes, the after-figures and
>   what is still open.
> - **Phase F1 (licence and privacy audit), implemented.**
>   - ONNX Runtime's telemetry, on by default in its official builds, is now switched off before it starts.
>   - The full licence texts ship in `licenses/`, and the notices cover all 49 packages.
>   - The [distribution checklist](../licence-checklist.md) is completed, with the decisions phases E and A will need.
>
>   See [ADR 0018](../decisions/0018-licences-and-no-telemetry.md).
> - **Phase F2 (logo and loading screen; added to M07 on request), implemented.**
>   - The mark is a blue print in the app's warm-white selection mat: the app and window icon, the sidebar, About and
>     the guide page.
>   - A splash appears the moment the app starts.
>   - While it starts and loads the library, the mark shows with what it is doing.
>
>   See [brand.md](../brand.md).
> - **Phase E (installer, updates, crash reports), implemented.**
>   - `build/publish.ps1` makes a self-contained ReadyToRun release (start-up 1.6 s, against 2.3–3.3 s for a debug
>     build), a portable ZIP, and a per-user Inno Setup installer.
>   - Updates are manual (a newer installer), with no online check.
>   - The database is backed up before upgrades.
>   - Crash reports are written locally and never sent, with a note on the next start.
>
>   See [ADR 0019](../decisions/0019-installer-updates-crash-reports.md).

## Goal
Evaluate and integrate RAW decoding and harden the app for professional use.

## Deliverables
- LibRaw integration spike with supported Canon RAW sample files
- Document supported formats/camera limitations
- RAW thumbnail/preview workflow
- Metadata preservation strategy
- Export profiles and naming templates
- Relocation/missing-drive handling
- Performance profiling and memory improvements
- Installer and update strategy
- Opt-in diagnostics/crash reporting design
- Commercial dependency/model licence audit

## Acceptance criteria
- RAW files decode on supported test cameras
- Unsupported/corrupt files fail gracefully
- Source RAW files remain untouched
- Export metadata behavior is documented
- Performance/memory benchmarks are recorded
- Distribution licence checklist completed

## Prompt
Implement only Milestone 07. Begin with a technical spike and compatibility report before integrating LibRaw. Verify native library deployment, architecture, licensing, and sample camera support. Do not promise universal RAW compatibility. Add tests and document supported formats and known limitations.

## Completion review (2026-11-26)

### Acceptance criteria
| Criterion | Met? | Evidence |
|---|---|---|
| RAW files decode on supported test cameras | ✅ | Canon EOS R50 CR3, the one camera tested. 12 of 12 samples decode in the spike, the tests and the app (import, thumbnails, viewer, Analyze, export), and in the release build. [ADR 0020](../decisions/0020-raw-through-libraw-previews.md) |
| Unsupported/corrupt files fail gracefully | ✅ | Corrupt CR3 files (zeroed header, cut-off copy, a text file named `.CR3`) are read errors, never crashes, and import lists them as Unreadable (tests). A wrong LibRaw version, a missing DLL or a missing VC++ runtime turns RAW off with a reason (tests, ADR 0019/0020). Photos in unsupported formats (`.NEF`, `.CR2`, `.HEIC`, …) are left out, and the import result counts them by extension (fixed after the review, see below). |
| Source RAW files remain untouched | ✅ | Export tests compare the RAW file's bytes before and after. After the A5 check (import, viewer, Analyze, export), all 24 copies still had the same SHA-256 as the user's originals. |
| Export metadata behaviour is documented | ✅ | [ADR 0017](../decisions/0017-export-naming-and-metadata.md) (JPEGs), ADR 0020 (RAW files), user guide § Export |
| Performance/memory benchmarks are recorded | ✅ | [m07-performance.md](../benchmarks/m07-performance.md) (before and after phase D), [m07-raw-spike.md](../benchmarks/m07-raw-spike.md), RAW viewer and export timings in ADR 0020 |
| Distribution licence checklist completed | ✅ for own use; 2 steps before sharing | [licence-checklist.md](../licence-checklist.md). Before FrameForge goes to anyone else: ship LibRaw 0.22.3's source (or a written offer), and confirm the Visual Studio licence that allows redistributing `vc_redist.x64.exe`. |

### Deliverables
| Deliverable | Where |
|---|---|
| LibRaw spike and compatibility report | `raw-spike` tool; [m07-raw-spike.md](../benchmarks/m07-raw-spike.md) |
| Supported formats and camera limits documented | ADR 0020; user guide § RAW files and § Limits |
| RAW thumbnail/preview workflow | ADR 0020 (A3): every decode uses the embedded full-size JPEG |
| Metadata preservation strategy | ADR 0017; ADR 0020 (A4) |
| Export profiles and naming templates | ADR 0017 (phase B) |
| Relocation/missing-drive handling | [ADR 0016](../decisions/0016-moved-folders-and-offline-drives.md) (phase C) |
| Performance profiling and memory improvements | m07-performance.md (phase D) |
| Installer and update strategy | [ADR 0019](../decisions/0019-installer-updates-crash-reports.md) (phase E) |
| Diagnostics/crash reporting design | ADR 0019: reports stay on the computer and are never sent; any future upload must be opt-in |
| Dependency/model licence audit | [ADR 0018](../decisions/0018-licences-and-no-telemetry.md), licence checklist, `THIRD-PARTY-NOTICES.md` (phase F) |

### Build, tests and release (run for this review)
- `dotnet clean` then `dotnet build -m:1`: 0 warnings, 0 errors.
- `dotnet test --no-build` with `FRAMEFORGE_TEST_RAW` and `FRAMEFORGE_TEST_MODELS` set: 864 passed, 0 skipped. Without
  them: 855 passed, 9 skipped (the opt-in tests on the user's files).
- `build/publish.ps1`: the first run stopped with "Tests failed" (the Release test run inside it), without saying
  which test. A Release test run on its own and a second publish both passed: 864, 0 failed, 9 skipped. The first
  failure was not reproduced and is unexplained.
- The release (`artifacts/`, version 0.1.0): `libraw.dll` matches the tested SHA-256; the LibRaw licence and copyright
  are in `licenses\`; the portable ZIP contains them; the installer bundles the VC++ runtime.
- The release build, on the A5 test library with its thumbnails deleted, made all 24 thumbnails and opened a CR3
  upright with its details, with nothing in the log beyond start and exit.

### Known limitations
- RAW: Canon CR3 only, tested with the Canon EOS R50 only. Edited through the embedded 8-bit JPEG (no sensor-data
  develop), and exported JPEGs of RAW files carry no GPS or other camera EXIF beyond the basic details.
- LibRaw runs inside the app process. No sample crashed it, but a native crash on some future file would close the app
  (the local crash report would record it).
- Before sharing the app: the two licence steps above.
- The intermittent "Tests failed" in the first publish run.

### Recommended next
- Find the intermittent Release test failure, by keeping the publish script's test output.
- Then the next milestone, which the roadmap decides.

### After the review
- **Import counts unsupported formats (2026-11-26).** The scan counts photos in formats FrameForge recognises but
  doesn't import (other RAW formats, HEIC, TIFF, PNG, WebP), by extension. The import result says so, for example
  "3 not imported because the format isn't supported (3 .NEF)", shown as a warning, and the log lists them. Other
  files (sidecars, videos, text) are still ignored without comment. Build: 0 warnings, 0 errors; tests: 868 passed,
  none skipped (4 new).
