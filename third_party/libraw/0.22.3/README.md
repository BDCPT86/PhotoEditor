# LibRaw 0.22.3 (unmodified)

FrameForge reads RAW files with [LibRaw](https://www.libraw.org/), a separate, replaceable DLL. It is loaded at run
time and is not modified.

| | |
|---|---|
| File | `libraw.dll` (64-bit Windows) |
| Taken from | `bin\libraw.dll` in the official binary package https://www.libraw.org/data/LibRaw-0.22.3-Win64.zip |
| Package SHA-256 | `4e2213a0b10e32199a30b9114a61376cdb40f94df861262b4a6dca659701276b` |
| `libraw.dll` SHA-256 | `2c4df36d9011267fd3ffd5c23e80d69d5f42c3d503f03ac02448f5c8a7fe0fa2` |
| Source code of this version | https://www.libraw.org/data/LibRaw-0.22.3.tar.gz (also .zip) |
| Needs | the Microsoft Visual C++ 2015–2022 runtime (x64). The installer adds it when missing (ADR 0019). |

## Licence
LibRaw is © 2008–2025 LibRaw LLC. It is available under either the GNU LGPL 2.1 or the CDDL 1.0
(`COPYRIGHT`).

**FrameForge uses it under the GNU Lesser General Public License, version 2.1** (`LICENSE.LGPL`), ADR 0020. What that
means here:
- **Separate DLL:** the library stays a separate DLL next to `FrameForge.exe`, linked only at run time.
- **Replaceable:** you may replace it with your own build of LibRaw 0.22.3 (for example, one you modified). FrameForge
  then logs that the file differs from the tested build. It refuses only a different LibRaw *version*, because it reads
  some of LibRaw's data structures directly, and their layout is version-specific (`FrameForge.Raw`).
- **Licence texts ship with the app:** `LICENSE.LGPL` and `COPYRIGHT`, in `licenses\`.
- **Source:** the source of this exact version is at the link above. Before giving FrameForge to anyone else, also ship
  that source archive with it or offer it in writing (LGPL 2.1, section 6); see `docs/licence-checklist.md`.
