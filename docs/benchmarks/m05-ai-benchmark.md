# FrameForge AI benchmark — 2026-11-16 19:45

- Machine: 20 logical CPUs · Microsoft Windows NT 10.0.26300.0 · X64
- Runtime: ONNX Runtime CPU, 2 intra-op threads per session, one photo at a time
- Face detector: yunet-face-detector@2026may#ebafce4e3c11
- Scene embedder: dinov2-small@hf-8b1f705#f22797eabf81

Synthetic 1024×683 images, 100 runs per model (inference + preprocessing; excludes JPEG decoding). First call (load + warm-up): 154 ms.

| Model | median ms | p95 ms | est. time per 1,000 photos (one photo at a time) |
|---|---:|---:|---:|
| YuNet face detector (640 px input, first run) | 14.7 | 24.4 | 15 s |
| YuNet face detector (1024 px input, adopted; re-run) | 36.1 | 50.2 | 36 s |
| DINOv2-small embedder | 86.3 | 90.5 | 86 s |

Peak working set: 298 MB at 640 px, 357 MB at 1024 px (whole process, both models loaded).

These are speed figures only. They say nothing about detection or grouping quality.

## Harness check on synthetic bursts (not a quality evaluation)

`tools/FrameForge.Evaluation evaluate` was run on 36 generated 1200×800 JPEGs (12 synthetic texture "bursts" × 3
frames, 1 s apart; the third frame softened), labelled by burst, with no keep/reject or face labels.

- Scene grouping (pairwise): precision 100 %, recall 88.9 % (32 of 36 pairs). Some softened frames fell below the 0.9
  similarity threshold.
- One face was detected on a synthetic texture (score ≥ 0.6). This is a false positive. It showed that a single face
  in a group used to block that group's pick, and that was fixed: faces now rank a group only when at least two
  members have faces.
- 2.0 min per 1,000 photos for decoding, deterministic sharpness and both models, one photo at a time; peak working
  set 249 MB.

Synthetic textures say little about real photos. A human-reviewed set is still needed before any claim is made about
quality.

## Real photos (user-supplied, 1,010 cricket JPEGs, local only)

- Face detection over the set, decoding at 1024 px plus detection: 119 s at a 640 px detector input, 142 s at 1024 px.
  Faces counted (score ≥ 0.6): 1,390 at 640 px, 2,167 at 1024 px. This is a visual check of annotated copies, not measured recall.
- Full **Analyze** in the app with both models on (isolated data folder):
  - deterministic pass: 33.6 s (33 ms/photo)
  - AI pass: 141.9 s (140 ms/photo, including its own decode)
  - UI message loop at most 7.8 ms throughout
  - result: 171 similar groups, 195 AI picks, 15 photos flagged *AI faces soft*
