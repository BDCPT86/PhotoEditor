# 0002 — Imaging library, thumbnail strategy, and virtualised grid

**Date:** 2026-11-16
**Status:** Accepted (Milestone 02)

## Context
Milestone 02 needs JPEG metadata, thumbnails, and full-resolution display, plus a grid that stays responsive with
thousands of photos. WPF has no built-in virtualising wrap panel. The product may be sold commercially, so library
licences matter.

## Decision
- **Imaging: Windows Imaging Component (WIC) through WPF's `System.Windows.Media.Imaging`.** No third-party
  imaging package. A separate `FrameForge.Imaging` project (net10.0-windows) keeps WPF imaging types out of
  Core, Application, and Infrastructure.
- **Metadata: raw EXIF tag queries** (`/app1/ifd/...{ushort=N}`) instead of Windows property-system names, for
  predictable types (rationals arrive as packed UInt64).
- **Thumbnails on demand, cached on disk.** They are generated when a tile is first shown, not during import.
  The cache is keyed by a hash of source identity plus settings, so it needs no database table and is safe to delete.
- **Bounded memory:** a 400-entry LRU of decoded thumbnails; tiles never hold bitmaps. Only the photo open in the
  viewer is decoded at full resolution.
- **Grid: `VirtualizingWrapPanel` 2.5.4** (MIT licence, targets net10.0-windows) inside a standard `ListBox`.

## Alternatives
- *ImageSharp:* capable and cross-platform, but the Six Labors Split License requires a paid licence for larger
  commercial users. That is a risk for a commercial product.
- *SkiaSharp:* MIT, but large native dependencies, and it would duplicate WIC's built-in JPEG and EXIF support.
- *MetadataExtractor:* good EXIF coverage (Apache-2.0). Not needed yet, since WIC reads the fields required here.
  Reconsider for RAW or MakerNotes.
- *Generate thumbnails during import:* would make import slower than needed (thumbnail decoding is ~10–50× the cost
  of reading a header) and spend work on photos the user may never scroll to.
- *Write a virtualising wrap panel:* about 300 lines of custom layout code, with scrolling and keyboard edge cases.

## Consequences
- Imaging and its tests are Windows-only (consistent with the product being Windows-first).
- WIC decoding cannot be interrupted part-way; cancellation is checked before and after each decode, and the viewer
  allows only one full-resolution decode at a time.
- RAW support (Phase 7) will need a different decoder behind the same ports.
- The disk thumbnail cache grows without limit until a cleanup policy is added.
