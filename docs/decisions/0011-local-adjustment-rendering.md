# 0011 — Rendering local adjustments

**Date:** 2026-11-18
**Status:** Accepted (Milestone 08 phase B, step B4)

## Context
Recipes can hold local adjustments (ADR 0009), and masks can be made and cached (ADR 0010). The renderer has to draw
them, and the M06 rules still apply:
- Preview and export use one code path, and the preview matches the downscaled export.
- A neutral recipe renders bit-identical to the source.
- Originals are never written.

The brief also asks for blending in linear light and a feathered, edge-aware mask without halos.

## Decision
- **One renderer.** `EditRenderer.Render` takes the masks by adjustment id. Without masks, or for a recipe with no
  local adjustments, it takes exactly the version 1 path, so neutral and global-only output is unchanged byte for byte
  (tested). The version becomes `render-v2`.
- **Order, per pixel, in linear light:**
  - the global settings first
  - then each local adjustment in order, as `c ← c + α·(local(c) − c)`, where `local` applies the adjustment's
    sliders with the same operations and ranges as the global ones
  - the result is encoded once at the end
  - A full mask therefore equals applying the same settings globally (within one level), and a local +1 EV on top of
    a global +1 EV is +2 EV (tested).
- **Masks follow the geometry.**
  - A mask covers the upright photo before crop. It is sampled through `EditGeometry.ToSource`, the mapping the
    pixels use, so it follows quarter turns, straighten and crop.
  - Inversion (Background) is applied here, so Subject and Background share one cached mask.
- **Edge-aware feather: a guided filter** (He, Sun and Tang), as a "fast guided filter":
  - The filter runs on a fixed working grid of 1024 px on the long side of the edited frame, whatever the output
    size. Radius 6, ε = 10⁻³ on brightness 0–1.
  - The guide is the photo's brightness, box-filtered from the source to about that density.
  - The filter's linear coefficients are sampled at each output pixel and applied to that pixel's own brightness.
    The edge is as sharp as the output, while preview and export share one mask field.
  - **What it does (measured on a step edge):** the steepest part of the mask's transition moves onto the photo's
    edge. Where the mask is a few pixels off, the strip is softened, roughly halved, rather than left as a hard halo.
    It does **not** fully correct a mask that is several pixels off; clicks (B5) are for that.
- **Missing masks:** an adjustment whose mask is unavailable is left out and the rest of the edit renders. The
  viewer and export get masks through `IEditRenderService`, which asks the mask service (computing and caching on
  demand) and logs each one left out, with the reason. `LoadMasksAsync` returns the reasons, so Develop can show them
  (B5).

## Alternatives
- **Blurring the mask (a plain feather):** rejected. It produces exactly the halo the brief warns about, because the
  blur crosses the photo's edges.
- **Filtering at the output resolution:** rejected. The memory and time grow with the export size (24 MP would need
  several GB for the filter's buffers), and preview and export would get different masks.
- **Upsampling the mask without refinement:** rejected. MobileSAM's 256 × 256 logits make visible stair steps at export
  size.

## Consequences
- **Tests:**
  - golden pixels for a local adjustment, cross-checked against the global equivalent inside the mask and the global
    settings alone outside it
  - geometry (crop, quarter turns, straighten)
  - the edge behaviour above
  - preview against the downscaled export (mean difference under 1.5 levels)
  - the earlier v1 golden values, still unchanged
- **Wiring (B5):** Develop's preview, the Auto option previews, the viewer and export draw local adjustments. Grid
  thumbnails draw them from cached masks only (loading the grid never starts a model); a thumbnail rendered before its
  mask was cached shows it once the recipe next changes. Export does not yet list adjustments it left out; they are
  only logged (B6).
- **No visual review yet:** the renderer has not been reviewed on the user's photos. It is done once masks can be
  added in Develop (B5) and recorded in the M08 visual review.
- **Speed:** not measured yet. The filter's cost is fixed (about a 1024 × 683 grid per mask, however large the
  export); the per-pixel cost of each mask is one more pass of the tone operations. It is measured in B7.
