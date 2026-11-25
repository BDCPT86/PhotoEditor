# M07 phase D — performance and memory

**Status:** D1 baseline and D2 fixes recorded 2026-11-25.

## How it was measured
- **Machine:** 20 logical CPUs, Windows 11 x64, Release build of the tool, Debug build of the app.
- **Headless:** `perf` in `tools/FrameForge.Evaluation`, with the real database, import and export services.

  ```powershell
  dotnet run --project tools/FrameForge.Evaluation -c Release -- perf --work <new folder> --sizes 1000,10000,50000 --photos <JPEG folder> --count 30 --out report.md
  ```

  - **Library scale:** generated 256 × 171 JPEGs with a capture date and camera, in folders of 500. 30 % are Keep
    (some rated), 10 % Reject and 5 % edited. Import reads only metadata, so small files stand in fairly for real ones.
  - **Real photos:** the first 30 of the user's 1,010 Canon EOS R50 JPEGs (24 MP), opened read-only.
- **In the app:** a UI Automation script on isolated data folders (`FRAMEFORGE_DATA_DIR`), using the generated
  libraries and the 1,010 real photos.
  - "Message loop" is how long the UI thread takes to answer a no-op message: under 50 ms feels instant.
  - Times include about 10–20 ms of polling.
  - Memory is the process working set (and private bytes).
- The real photos stay outside the repository; exports went to a scratch folder.

## D1 baseline

### Library scale

| Photos | Import | Database | Load photos | Load decisions | Load edits | Managed memory for the list |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 0.6 s | 1.5 MB | 11 ms | 2 ms | 1 ms | 0.9 MB |
| 10,000 | 2.0 s | 12.8 MB | 68 ms | 11 ms | 4 ms | 9 MB |
| 50,000 | 7.8 s | 47.9 MB | 296 ms | 37 ms | 8 ms | 45 MB |

The 1,010 real photos imported in 5.6 s in the app.

| In the app | 1,010 real | 10,000 | 50,000 |
|---|---:|---:|---:|
| Start to a filled grid | 2.4 s | 2.7 s | 4.1 s |
| Working set when idle after start | 334 MB | 349 MB | 430 MB |
| Switch view (Kept, Rejected, All) | – | 330–360 ms | 300–510 ms |
| Change sort | – | 470–480 ms | 385–445 ms |
| Grid scrolling (30 pages): UI response after each page | under 2 ms | under 5 ms | under 3 ms |
| Working set after scrolling | 578 MB (15 pages) | 569 MB | 682 MB |

- **Database loads** grow linearly and stay small: 0.3 s for 50,000 photos.
- **Start-up** is mostly a fixed cost of about 2.3 s, and is a little longer at 50,000.
- **Switching view or sort** blocks the UI for 0.3–0.5 s at 10,000 photos and at 50,000. Most of that is a fixed cost,
  not one that grows with the library.
- **Scrolling** never blocks the UI. Thumbnails load in the background.

### Per photo (real 24 MP JPEGs)

| Step | Median (90th percentile) | Pixels held |
|---|---:|---:|
| Read metadata (import) | 2.3 ms (3.0) | – |
| Make a 320 px thumbnail | 74 ms (83) | – |
| Viewer decode at full size (what the viewer does now) | 385 ms (409) | 92 MB |
| Decode to 2560 px (fit view, 1440p or 4K) | 159 ms (173) | 17 MB |
| Decode to 1920 px (fit view, 1080p) | 131 ms (146) | 9 MB |

**Viewer in the app (1,010 real photos):**
- First photo shown in 440–510 ms.
- **Next photo: median 775 ms, worst 853 ms.** A one-second look before pressing → doesn't help, because nothing is
  loaded ahead.
- Working set 780 MB while viewing. It stays there after the viewer closes.

### Memory around an edited export (full size, quality 92)

| | Headless (30 photos) | In the app (20 photos) |
|---|---:|---:|
| Time per photo | 0.57 s | 0.59 s |
| Working set before | 138 MB | 683 MB |
| Peak during | 509 MB | 1,009 MB |
| Right after | 428 MB | 917 MB |
| Later | 325 MB after a forced full GC | 917 MB after 30 s idle |

Headless, 95 MB of managed memory (one 24 MP image) looked reachable after a full, compacting GC. **D2 found this was
the measurement, not a leak.** The harness continued inline when the export finished, so the export's last stack frame
was still live. Measured from another thread, nothing is left (see D2). In the app, nothing is given back while it sits
idle.

### Disk caches
Thumbnails take about 21 KB each, so about 1 GB at 50,000 photos. Neither the thumbnail nor the mask cache has a size
limit.

## Proposed targets and D2 work

| # | Problem | Target | Likely fix |
|---|---|---|---|
| 1 | Next photo takes about 780 ms; 92 MB per photo | **≤ 150 ms** when the previous photo was looked at for 1 s; **≤ 400 ms** otherwise | Decode to screen size for the fit view, and at full size only at 100 %. Load the next and previous photos ahead, within a fixed budget. |
| 2 | Memory isn't given back after the viewer or an export | Back within **150 MB** of the starting working set, within a few seconds | Find what holds the last image (headless: 95 MB reachable). Release viewer bitmaps on close. |
| 3 | Peak memory during an export: about 1 GB | **≤ 800 MB** | Follows from 2. Check how many full-size buffers live at once. |
| 4 | Switching view or sort blocks the UI for 0.3–0.5 s | **≤ 150 ms** at 10,000 photos; ≤ 300 ms at 50,000 | Profile where the time goes (rebuilding the tile list, grouping, layout), then fix that. |
| 5 | Disk caches have no size limit | A limit (proposed 2 GB in total), removing the least recently used | Prune at start-up and after large jobs. |
| – | Start-up of 2.4–4.1 s | Already within the 3 s target at 10,000. Not worked on unless 4 shows a shared cause. | – |

## D2: what was found and changed

### Where the memory went
- **Method:** ClrMD (Microsoft.Diagnostics.Runtime 3.1, already in the local NuGet cache) was attached read-only to the
  running app. It reported **about 17 MB of reachable managed objects** throughout: after start, after the viewer and
  after an export. Nothing was leaking.
- **What was left after an export:** 199 MB of large arrays that were already garbage. Large objects are reclaimed only
  by a full collection, and an idle app gives the garbage collector no reason to run one.
- **What was left after the viewer:** about 165 MB, almost all native. These are bitmap pixels held by WPF objects that
  are waiting to be collected, and each full-size decode was 92 MB.

### Changes
| Area | Change |
|---|---|
| Viewer | Photos are decoded to the size of the image area (in device pixels, rounded up to 512; 2048 px on the test screen) instead of full size. The next photo in the direction of travel and the one behind are loaded ahead. At most 4 finished images are kept, about 11–17 MB each. The photo you look at always loads at once, never behind a look-ahead. |
| Viewer zoom | The image area knows the photo's full size, so "100 %" still means real pixels. When the zoom enlarges the screen-size image, the full-resolution image (or full-size edit render) is loaded for that photo only and swapped in, keeping the zoom and position. |
| Memory | After the viewer closes and after an edited export, one aggressive garbage collection runs on a worker thread (`MemoryRelief`). |
| Grid | Analysis badges and problem marks are built only for tiles that have them. The grid keeps half a page less off-screen, a quarter of a page each side. Sorting by folder ranks the folders once instead of comparing full paths. |
| Disk caches | Thumbnails and masks together are kept under **2 GB**. The oldest-written files are removed down to 80 % after start-up, in the background. A thumbnail removed while the grid is reading it is made again once. |
| Tool | `perf` measures memory from another thread (see above). |

Tried and dropped: **reusing the grid's collection** (one Reset instead of a new collection), hoping the recycling panel
would reuse its tiles. Timings didn't change. The change is kept, because it is simpler for the grid, but it isn't
claimed as a speed-up.

### After (same machine, same data)

**Viewer (1,010 real photos)**
- The app's own timing, a temporary log from the key press to the image being set:
  - **next photo 0–3 ms** when it was loaded ahead, at 0.4 s per photo and with a 1 s look
  - first photo 140–170 ms
- The UI Automation script that measured the baseline reports 337–352 ms per photo (baseline 775 ms). Most of that is
  the script itself: it searches the window's automation tree, about 0.3 s per search.
- Zoom to 100 %: the full-resolution image is shown about 1.1 s after the key, including about 0.4 s of script delay.
- First open: the first image may be made before the window reports its size. The viewer then reloads it at screen
  size (310 ms), not at full size.

**Memory (working set)**
| | Before (D1) | After |
|---|---:|---:|
| Viewing 20 photos | 528–780 MB (two runs; start 334–373) | 568–578 MB (start 360) |
| After closing the viewer | 538–780 MB, about 165–445 MB above the start | 533 MB after 15 s, about 170 MB above the start |
| Edited export of 20 photos: before → peak → after | 683 → 1,009 → 917 MB | 1,090 → 1,274 → 721 MB |

The after-run's export started from 1,090 MB because it came after the viewer and a 100 % zoom. It ended 370 MB lower
than it started. Before the change, it ended 234 MB higher.

**Grid**
| | Before (D1) | After |
|---|---:|---:|
| Query + layout per view or sort change, 10,000 photos (app's own timing) | not measured separately; 300–630 ms to idle | 140–190 ms |
| Sort by folder, query only, 10,000 photos | 80–88 ms | 24–26 ms |
| Change sort, UI Automation timing, 10,000 / 50,000 | 470–480 / 385–445 ms | 185–250 / 187–346 ms |
| Switch view, UI Automation timing, 10,000 / 50,000 | 330–360 / 300–510 ms | 254–352 / 368–466 ms |
| Scrolling: UI response after each page | under 5 ms | under 3 ms |

### Targets
| # | Target | Result |
|---|---|---|
| 1 | Next photo ≤ 150 ms after a 1 s look; ≤ 400 ms otherwise | **Met:** 0–3 ms when loaded ahead; 140–170 ms for a photo not loaded ahead |
| 2 | Memory back within 150 MB of the start | **Export: met**, it ends below where it began. **Viewer: not quite**, about 170 MB stays, in native WPF memory |
| 3 | Export peak ≤ 800 MB | **Not met in this run** (1,274 MB, starting from 1,090). The export's own rise fell from +326 MB to +184 MB |
| 4 | View or sort ≤ 150 ms at 10,000 photos | **Close:** query + layout is 140–190 ms. The rest is building about 30 visible tiles (3–5 ms each) |
| 5 | Disk cache limit | **Done:** 2 GB |

### Still open
- **Viewer memory after closing:** about 170 MB is held in native WPF memory (rendering resources), not managed objects.
- **Tile cost:** each tile still takes 3–5 ms to build, because the template is large. Making it cheaper would mean
  redesigning the tile.
- **Settings:** the cache limit is fixed at 2 GB. It isn't shown in Settings, and there is no "Clear cache" button.
