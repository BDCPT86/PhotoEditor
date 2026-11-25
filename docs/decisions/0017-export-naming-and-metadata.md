# 0017 — Export naming, metadata and profiles

**Date:** 2026-11-25
**Status:** Accepted (Milestone 07, phase B)

## Context
Before this phase:
- Exported files always kept the original's name, with " (2)" added when it was taken.
- Edited JPEGs had one metadata switch, "Keep camera metadata". It copied everything from the original, including GPS
  location. It reset orientation and dropped the embedded preview.
- Nothing said what happened to each kind of metadata.

While writing this down, checks with generated JPEGs found an existing fault: **an original with an embedded ICC
profile exported with wrong colours**. The photo loader (WPF `BitmapImage`) applies an embedded profile and hands over
sRGB pixels. But the copied metadata included the original's APP2 profile segment (WIC exposes it as an `/unknown`
block). So the new file carried sRGB pixels labelled, say, Adobe RGB, and colour-managed viewers converted them a
second time.

## Decision

### File names (`ExportNameTemplate`, Application layer)
- **Where it applies:** copied originals and edited JPEGs. The manifest lists the source files, so it doesn't use it.
- **The pattern:**
  - An empty pattern keeps the original names, exactly as before.
  - Tokens are `{name}`, `{date}`, `{time}`, `{seq}`, `{folder}`, `{camera}`, `{rating}`, `{decision}` and `{label}`.
  - `{date}` and `{time}` use when the photo was taken, falling back to the file's modified time (local).
  - Formats: `{date:…}` and `{time:…}` take .NET date formats, applied with the invariant culture so names don't depend
    on Windows' language. `{seq:N}` pads to N digits; the default is 3.
- **Cleaning up names:**
  - A token with no value is left out, together with one separator (`-`, `_`, space or `.`) next to it.
  - Characters Windows rejects are an error when typed in the pattern. Coming from a token value, they become `_`.
  - Trailing dots and spaces are removed, device names (`CON`, `NUL.x`…) get `_`, and names are cut to 120 characters
    without splitting an emoji.
- **Clashes:** handled after naming by `ExportNamePlanner`, as before (" (n)", case-insensitive, never overwrite).
  "Renamed" in the summary now means only that: a clash.
- **Not supported:** subfolders in the pattern (`/` and `\` are errors).

### Metadata in edited JPEGs (`ExportMetadata`, `WicJpegEncoder`)
**Copied originals are always byte-for-byte identical**; everything below is about edited JPEGs.

There are three choices: **All**, **All except location** and **None**, plus **"Include my star rating and colour
label"** (on by default; no effect with None).

| What | All | All except location | None |
|---|---|---|---|
| EXIF camera and exposure details, date taken | kept | kept | — |
| XMP (titles, keywords, creator, …) | kept | kept, minus location fields | — |
| IPTC IIM (caption, keywords, …) | kept | kept, minus location fields | — |
| EXIF GPS | kept | removed (whole GPS block) | — |
| XMP location: `exif:GPS*`, `photoshop:City/State/Country`, `Iptc4xmpCore:Location/CountryCode`, `Iptc4xmpExt:LocationCreated/LocationShown` | kept | removed | — |
| IPTC location: City, Sub-location, Province/State, Country code and name | kept | removed | — |
| Orientation (EXIF and XMP) | set to 1 (pixels are upright) | set to 1 | — |
| Embedded EXIF thumbnail | removed (shows the unedited photo) | removed | — |
| ICC profile (APP2) | removed; pixels are already sRGB | removed | — |
| EXIF/XMP colour space | sRGB if the original had an ICC profile; otherwise unchanged | same | — |
| Other unrecognised segments (e.g. Canon's multi-picture index) | removed (they describe the original file) | removed | — |
| Rating (EXIF 18246 + XMP `xmp:Rating`) and label (XMP `xmp:Label`) | FrameForge's, replacing the original's; unrated or no label clears them | same | — |

**Colour space.** The loader converts pixels to sRGB only when the original embeds an ICC profile. A camera's Adobe
RGB JPEG is usually marked only by EXIF tags (colour space "uncalibrated" plus the DCF interop index "R03"). The loader
doesn't convert those, so their pixels stay Adobe RGB and the tags are kept to say so. A raw scan of the JPEG segments
(`JpegSegments`) tells the two cases apart, because WPF reports a colour context for both.

**Failures.** If the original's metadata can't be read, the JPEG is still written. It gets only the rating and label,
and the export log counts it as "without metadata".

### Saved export profiles (`ExportProfile`, `ExportProfileService`)
- **What a profile holds:** the export mode, the file-name pattern, and the edited-JPEG settings (quality, longest side,
  metadata choice, rating and label). **Not** the destination folder (it is chosen for each export) and not which photos
  are exported.
- **Built-in profiles** can't be changed or removed, and their names can't be reused:

  | Profile | Mode | Settings |
  |---|---|---|
  | Full-size JPEG | Edited JPEGs | quality 92, full size, all metadata, rating and label |
  | Web (2048 px, no location) | Edited JPEGs | quality 85, longest side 2048, all except location, no rating or label |
  | Originals | Copy files | original names |

- **The user's profiles:** saved under a name (trimmed, up to 60 characters). Saving with the name of an existing
  profile of the user's replaces it (names are compared ignoring case). Only these can be removed.
- **Last used:** when an export starts, its settings are remembered, and the panel opens with them next time. The panel
  shows the first profile that holds exactly the current settings, and "Changed from this profile" once a setting
  differs from the chosen one.
- **Storage:** one setting, `export.profiles`, holding a JSON document with a `Version` (1), the user's profiles and the
  last-used settings. It goes in the existing settings table, so no database migration is needed. Enums are stored by
  name. A document that can't be read, has another version, or breaks the rules (a built-in or repeated name, an invalid
  profile) is ignored and logged, and the panel falls back to the built-ins.

## Alternatives
- **One setting per profile, or a profiles table:** a table needs a migration; separate keys can't be listed through
  the settings port. A single document is small (a few profiles) and is replaced in one write.
- **Storing the destination folder in a profile:** rejected. A remembered folder makes it easy to export into the
  wrong place, and the folder is checked against the library on each export anyway.
- **Carry the ICC profile into the output** (decode with `IgnoreColorProfile`): rejected for now. It would change how
  every photo is loaded for preview and analysis, not just exports. Converting once on load and writing sRGB keeps
  preview and export the same.
- **Embed an sRGB profile in every export:** not needed. Untagged JPEGs are read as sRGB, and it adds about 3 KB per
  file.
- **Remove the whole IPTC block for "except location":** simpler, but it would also lose captions and keywords.
- **A per-token "if empty" syntax for names:** more than needed. Dropping one separator covers the common patterns.

## Consequences
- Exports of photos with an embedded profile now show the right colours.
- Location removal covers the standard places. Location written in camera maker notes or in non-standard XMP fields is
  not looked for. The tests check that the place name appears nowhere in the file, for the fields they set.
- Camera Adobe RGB JPEGs (EXIF-marked) keep their colour space, but FrameForge itself shows and edits their pixels as if
  they were sRGB, as it always has. Colours in the app look a little flat; other apps show the export correctly.
- Metadata in XMP sidecar files (`.xmp` next to the original) is not read.
- Copied originals are untouched, so "All except location" doesn't apply to them; the panel says so.
- Stored profiles that can't be read are ignored, not repaired. The next save replaces them with a new document, so
  they are lost then (the log says when this happens).
- Profiles are not checked against each other: two profiles may hold the same settings, and then the first one is shown.
