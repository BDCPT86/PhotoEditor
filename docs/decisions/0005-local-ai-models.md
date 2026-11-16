# 0005 — Optional local AI: runtime, models, consent and caching

**Date:** 2026-11-16
**Status:** Accepted (Milestone 05)

## Context
Milestone 05 adds local AI as **optional recommendations**. The app must still work with no model installed. Model
errors must not break browsing or culling. AI must never overwrite user decisions. Models must never be downloaded
silently. Every model needs its source, version, licence and checksum recorded. The user approved the plan on the
condition that **no model or runtime carries a fee**.

## Decision

### Runtime
**Microsoft.ML.OnnxRuntime 1.31.1**, CPU execution provider, in a separate `FrameForge.AI` project that implements
the Application ports (`IAiRuntime`, `IFaceDetector`, `IImageEmbedder`).
- Licence: MIT, free.
- Version: 1.32.0 was published too recently for the project's "at least two weeks old" rule, so 1.31.1 is used.
- DirectML/GPU is deferred until it is validated (see Alternatives).

### Models
| | YuNet face detector | DINOv2-small image embeddings |
|---|---|---|
| Purpose | Face boxes + 5 landmarks (detection only, no recognition or identity) | 384-d scene embedding for grouping similar shots |
| Source | OpenCV Zoo `models/face_detection_yunet/face_detection_yunet_2026may.onnx` at commit `26cc381e4d2594bb9f47a26eb8fd96c94a13660d` | `onnx-community/dinov2-small` `onnx/model.onnx` at revision `8b1f705a3a7f6f062f6bdd21986c1583d3ef105d` (ONNX export of Meta's `facebook/dinov2-small`) |
| Version tag | `yunet-face-detector@2026may#ebafce4e3c11` (results tagged `…@in1024`, the input size) | `dinov2-small@hf-8b1f705#f22797eabf81` |
| Size | 229,738 bytes | 88,532,934 bytes |
| SHA-256 | `ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0` | `f22797eabf810a75e41de68d378541ebea372122b25c4ce3ef25ff618250c20a` |
| Licence | MIT — Copyright (c) 2020 Shiqi Yu | Apache-2.0 — Meta Platforms, Inc. and affiliates (code and weights); the conversion repository has no separate licence tag |
| Fee | None | None |
| I/O (verified with ONNX Runtime) | `input` [1,3,H,W] BGR 0–255; 12 outputs `cls/obj/bbox/kps_{8,16,32}` | `pixel_values` [N,3,224,224]; `last_hidden_state` [N,257,384] |
| Speed (this machine, CPU, 2 threads) | median 36.1 ms/photo at 1024 px input | median 87.7 ms/photo |
| Quality evaluation | Visually checked on 1,010 real photos (see below); recall **not yet measured** | **Not yet evaluated** on a human-reviewed set |

### Acquisition
- Models are **not bundled**. The user installs them in one of two ways:
  - **Download**, through a consent panel that shows the size, the pinned URL, the SHA-256 and the licence. The
    store refuses a download without a matching consent record.
  - **Import** of a file they already have.
- Both paths verify the SHA-256 before the file is used. A mismatch is never installed.
- The consent time is logged. No user data is sent; the request is a plain GET of the pinned URL.

### Caching
Results go in the existing `AnalysisResults` table (kinds `Faces` = 10, `SceneEmbedding` = 11). The model's
`VersionTag` (which includes the SHA-256 prefix) serves as the algorithm version, so replacing a model file
invalidates its results.

### Advisory output
- AI writes only to `AnalysisResults`. Insights (faces, soft faces, scene groups, suggested pick with a confidence
  band) are computed in memory, shown as badges, filters and explanation lines, and carry an explicit "not yet
  evaluated" disclaimer.
- Capabilities are **off by default**.

## Alternatives
- **SCRFD / RetinaFace (InsightFace):** rejected. The pretrained weights are licensed for non-commercial research only.
- **YOLO-family detectors (Ultralytics):** rejected. AGPL-3.0 or a paid enterprise licence; the user requires no fees.
- **CLIP (OpenAI):** rejected for now. The model card advises against deployed use, and grouping does not need text
  alignment.
- **Bundling models in the installer:** rejected. It adds 89 MB for users who may never enable AI, and makes consent
  implicit.
- **DirectML / GPU execution provider:** deferred. The CPU baseline is fast enough for batch culling. A GPU provider
  adds a native dependency, driver variability and validation work.
- **OpenCV `FaceDetectorYN` instead of hand-written decoding:** rejected. It would add a large native dependency for
  roughly 60 lines of decoding, which are unit-tested against synthetic tensors.

## Consequences
- The app gains a native dependency, `onnxruntime.dll` (16.5 MB for win-x64), even when no model is installed.
- Model quality is **unknown** until a human-reviewed evaluation set exists. `tools/FrameForge.Evaluation evaluate`
  computes grouping precision and recall, face recall, pick agreement, false-rejection rate, latency and memory once
  such a set (photos plus a labels CSV) is provided.
- **YuNet on real photos (2026-11-16):** the user supplied a 1,010-photo cricket set, which was used locally only and
  not stored in the repository. Annotated copies were inspected:
  - Boxes and landmarks sat on faces in group shots, single portraits and portrait orientation, and helmeted batters
    were found.
  - At a **640 px** input, distant fielders and spectators were mostly missed. **1024 px** found 56 % more faces
    (2,167 vs 1,390). Every extra detection inspected was a real face. Cost: 36 ms instead of 15 ms per photo.
    Adopted.
  - The back of a head was detected at score 0.52, below the 0.6 threshold, so it was not counted.
  - In the app, 15 of the 1,010 photos were flagged "AI faces soft". Most were plausible: several were also flagged
    blurry by the heuristic.
  - **Known weakness:** Laplacian sharpness depends on contrast. One in-focus face in deep shade scored 20 (soft).
    "Faces soft" can therefore misfire on dark, low-contrast faces; tune the threshold or treat it as a hint.
  - This is a visual check, not measured recall or precision. That still needs labelled photos.
- **Main faces:** softness and the pick use only faces at least half the width of the largest face and at least 16 px.
  Otherwise a blurred spectator, or a crop too small to measure, would decide the result.
- DINOv2 pre-processing uses bilinear rather than bicubic resizing, a small documented difference from the reference.
- Face results are not identity data: no recognition or face embeddings are computed or stored.
