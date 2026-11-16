# Milestone 04 — Deterministic Image Analysis

## Goal
Add explainable, non-AI image metrics and duplicate grouping.

## Deliverables
- Sharpness/blur heuristic (clearly labelled as heuristic)
- Exposure/clipping indicators where feasible
- Exact duplicate detection by hash
- Near-duplicate detection using perceptual hash
- Burst/near-duplicate grouping UI
- Configurable thresholds
- Analysis queue, cancellation, progress, caching
- Versioned analysis results

## Acceptance criteria
- Same image/settings/algorithm version returns cached result
- Changing settings or algorithm version invalidates cache
- Analysis runs off UI thread with bounded concurrency
- User can inspect why a score/group was produced
- False positives can be reviewed; no automatic deletion
- Tests cover hashing, thresholds, caching keys, cancellation

## Prompt
Implement only Milestone 04. Start with deterministic techniques and avoid calling them AI. Benchmark on a small test fixture. Add an analysis service abstraction and persist versioned results. Do not add face detection, embeddings, or model downloads yet. Build, test, and document limitations.
