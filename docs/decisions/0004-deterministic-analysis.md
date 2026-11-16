# 0004 — Deterministic analysis: metrics, caching, and grouping

**Date:** 2026-11-16
**Status:** Accepted (Milestone 04)

## Context
Milestone 04 adds explainable, non-AI image metrics (sharpness, exposure), exact and near-duplicate detection, and
grouping. Results must be cached per image, settings and algorithm version. Analysis must run off the UI thread with
bounded concurrency, and users must be able to see why a score or group was produced. Nothing may be deleted
automatically.

## Decision
- **Hand-written managed algorithms in a separate `FrameForge.Analysis` project** (net10.0, depends only on Core):
  - Laplacian variance per 4×4 grid cell, scored by the sharpest cell
  - channel-based clipping percentages
  - 32→8 DCT perceptual hash
  - Hamming distance with union-find grouping
  - SHA-256 of the file bytes for exact duplicates

  No OpenCV or other native dependency.
- **One decode per photo** at an analysis resolution (longest side 1,024 px by default), oriented upright, feeds all
  image metrics.
- **Two kinds of settings:**
  - *Computation* settings (resolution, grid) go into a per-kind settings hash, so changing them makes results stale.
  - *Interpretation* thresholds (blur score, clipping limits, hash distance, burst window) are applied in memory, so
    changing them never triggers re-analysis.
- **Cache validity** = algorithm version + settings hash + source size + source modified time. A file changed and
  re-imported is therefore re-analysed.
- **Groups are not persisted.** They are recomputed from cached hashes whenever results or thresholds change.
- **Explanations are produced together with the flags** (`AnalysisInsights`). Every badge has a details entry with the
  value, the threshold and the method.

## Alternatives
- *OpenCvSharp:* mature algorithms, but a large native dependency for what is a few dozen lines of arithmetic here.
  Reconsider for feature matching or SSIM.
- *dHash or aHash instead of DCT pHash:* simpler, but less robust to brightness and compression changes.
- *Persist groups:* would allow user corrections, but groups would go stale on every threshold change. Deferred until
  corrections are needed.
- *Thresholds in the settings hash:* simpler cache rule, but every threshold tweak would force a full re-analysis.

## Consequences
- Default thresholds are starting points, calibrated only on synthetic fixtures. Users need to tune them for their
  cameras.
- Union-find chaining can join photos that are not directly similar. Each member shows its nearest link and distance.
- With the burst window disabled, grouping compares all pairs: about a second for 22,000 photos, quadratic beyond that.
- WIC decoding differences between Windows versions could shift perceptual hashes by a few bits. Exact duplicates use
  file bytes and are not affected.
