# M07 A1 — RAW decoding spike (LibRaw 0.22.3, Canon EOS R50)

**Date:** 2026-11-25
**Tool:** `raw-spike` in `tools/FrameForge.Evaluation` (Release build). RAW files are opened read-only; the report and
pictures go to a folder outside the repo.

```powershell
dotnet run --project tools/FrameForge.Evaluation -c Release -- raw-spike --raw <CR3 folder> --jpeg <camera JPEG folder> --libraw <libraw.dll> --out-dir <new folder> --out report.md
```

## Setup
- **Samples:** 12 CR3 files from a Canon EOS R50 (6192×4060 sensor data), each with the camera's own JPEG of the same
  shot. The JPEGs have other file names, so they are paired by capture time (all 12 paired, none ambiguous). The samples
  are not in the repo.
  - Orientation: 5 landscape and 7 portrait (LibRaw flip 5 on 4 files, 6 on 3).
- **LibRaw:** 0.22.3, the official Windows x64 build (`libraw.dll`, SHA-256
  `2C4DF36D9011267FD3FFD5C23E80D69D5F42C3D503F03AC02448F5C8A7FE0FA2`). Its camera list (1,258 models) includes the
  Canon EOS R50.
- **Windows:** no WIC codec for CR3 is installed, so Windows can't decode these files itself.
- **Machine:** 20 logical CPUs, Windows 11 x64.

## Results

**Decoding:** 12 of 12 files opened, gave their embedded preview, and developed. The developed size is 6000×4000 (or
4000×6000), with the orientation applied.

**Speed** (median, with the 90th percentile in brackets):

| Stage | ms |
|---|---:|
| Open and read the header | 1 (1) |
| Extract the embedded preview (a full-size 6000×4000 JPEG in every file) | 2 (3) |
| Decode that preview at full size (viewer) | 253 (295) |
| Decode that preview at 400 px (thumbnail) | 35 (42) |
| Develop the sensor data (unpack, demosaic, 8-bit sRGB) | 2,454 (2,566) |

**Memory:** the process working set peaked at 879 MB while developing one file (median 781 MB).

**Colour against the camera's JPEG** (CIE76 ΔE in Lab at 200 px; under about 2 is hard to see, 5–10 is a clear
shift):

| Image | Average-colour ΔE, median (max) | Lightness, median |
|---|---:|---:|
| Embedded preview | 0.1 (0.2) | +0.1 |
| LibRaw develop, auto brightness | 7.8 (20.3) | −5.8 |
| LibRaw develop, no auto brightness | 22.7 (26.7) | −23.1 |

The embedded preview is the camera's own rendering. LibRaw's develop has no Canon picture style or tone curve, so it
looks darker and flatter.

**Damaged copies** (each decoded in a child process, so a native crash would end only that process):

| Damage | Result |
|---|---|
| Empty file; first 1 %, 10 %, 50 % or 90 % only | refused cleanly (open or unpack error) |
| First 4 KB zeroed; random noise of the same size | refused cleanly ("unsupported file format") |
| 4 KB zeroed at 1 %; 1,000 or 10,000 random bytes changed | decoded anyway, with damaged pixels |

No damaged copy crashed LibRaw.

## What this means for phase A
- **Culling RAW files can use the embedded preview.** It is full size, matches the camera JPEG, and is as quick as a
  JPEG to show. Thumbnails can come from it too.
- **Editing RAW files needs more work.** Developing the sensor data takes about 2.5 s and up to about 880 MB, and its
  look differs from the camera's. It would need its own tone curve, a smaller working copy for live editing, and
  caching.
- **Damaged files:** LibRaw reports errors for truncated or garbled files. Some damaged files decode with broken pixels
  instead of failing; that can't be detected from the error code.
- **Not tested:** other Canon models, and CR2 or DNG files. The spike doesn't record the samples' ISO, so high-ISO files
  weren't checked on purpose. Only these 12 samples were checked, so this is no promise of wider RAW support.
