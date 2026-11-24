# 0014 — Mask speed, memory and evaluation

**Date:** 2026-11-23
**Status:** Accepted (Milestone 08 phase B, step B7, 2026-11-24): mask models at 4 threads, and sky rules version 2.

## Context
The brief asks for latency and memory per photo on the target machine (goal: a subject mask in under 1 s on CPU for a
24 MP photo), mask IoU against hand-drawn masks, and the sky false positives found in B5 to be looked at. Earlier
figures (4.6 s in ADR 0008, 2.4 s in ADR 0010) came from single runs with every model at 2 threads.

## Method
- `mask-benchmark` in `tools/FrameForge.Evaluation` runs the app's own classes (`SubjectMaskGenerator`,
  `SkyMaskGenerator`, `EditRenderer`) on 20 of the user's 24 MP JPEGs. It times each stage: the 1024 px decode, find
  people, encoder, decoder, sky, and the full-size decode and render with a mask.
- **Machine:** i7-13700H laptop (14 cores, 20 threads), ONNX Runtime CPU.
- **Interleaving:** each photo runs every setting in a rotating order. In back-to-back runs, the machine's load alone
  (power mode, other apps, heat) changed every stage by up to 3–5×, decoding and plain rendering included. That made
  runs of different settings incomparable. Interleaving keeps the comparison fair; the absolute times still depend on
  the machine's state.

## Results
| Subject mask (find people, encode, decode), median | 2 threads | 4 threads | 6 threads |
|---|---:|---:|---:|
| Quiet machine | 0.95 s | **0.52 s** | – |
| Busy machine (interleaved) | 4.7–4.9 s | 2.5–2.7 s | 3.0 s |

- **Threads:** 4 threads made a subject mask 1.8× faster than 2 in every interleaved run. 6 was slower than 4,
  probably because the extra threads land on the CPU's efficiency cores.
- **The brief's goal** (under 1 s) is met at 4 threads when the machine is quiet: 0.52 s median, of which 0.07 s is
  finding people, 0.36 s the encoder and 0.08 s the decoder. On a busy machine it takes about 2.5 s.
- **A click** (decoder only) takes 0.08–0.4 s at 4 threads.
- **Sky mask:** 25–110 ms median, plus the 1024 px decode (90–300 ms).
- **Export:** the full-size decode takes 0.4–1.5 s. A full-size render with one mask takes 0.13–0.5 s, against
  0.04–0.12 s without masks.
- **Memory:**
  - **Masks:** with the three mask models loaded and masks made, the process peaked at 474 MB.
  - **Full-size images:** after the full-size decodes and renders it peaked at 1.3 GB. A full-size render with a mask
    allocates 143 MB; most of the rest is likely large image buffers from earlier photos that .NET had not yet
    reclaimed (not measured further).

## Decisions
- **Mask models use 4 threads** (`OnnxAiRuntime.MaskIntraOpThreads`). Face detection and scene analysis stay at 2,
  because they run in the background during import. One mask runs at a time, and the user is waiting for it.
- **Batch estimate:** 0.6–3 s per photo for a subject and 0.2–0.6 s for sky (was 2–5 s and 0.3–1 s). The panel's text
  takes its numbers from the same constants.
- **Not done:**
  - **Decoding the next photo while the current one is masked:** it saved about 8 % (0.06–0.25 s a photo) and
    nothing at 6 threads. Not worth the extra code in the batch loop.
  - **Turning off ONNX Runtime's thread spinning:** the medians were the same (0.53 s and 0.52 s); only the slowest
    10 % was faster in one run (1.8 s against 2.7 s). That's not enough to change the app; it stays at the default.

## Consequences
- **Machine state matters more than any setting tested.** A subject mask can take 5× longer when the laptop is busy
  or throttled. The batch estimate's range covers that, and its countdown uses the measured speed.
- **4 threads compete with background work:** a mask computed during an import's analysis competes with it for cores
  (4 for the mask plus about 4 for analysis). That is still well under the 14 cores.
- **The full-size memory peak** in the benchmark (1.3 GB) is worth a look when RAW support (M07) brings larger images.

## Evaluation
### Sky, round 1 (`sky-rules@1`, 30 photos, seed 1)
- **Sample:** a blind grading sheet (`sky-sheet`): 20 photos where the rules found sky, 10 where they didn't.
- **Found (20):** good 3, usable 7, bad 10; good or usable 50 % (95 % interval 30–70 %).
- **Not found (10):** all right; no visible sky was missed.
- **Why so many bad:** 8 of the 10 bad masks covered pale or blue walls, panels or hazy brick touching the top edge,
  and 2 were roof edges next to real sky. The overcast-sky colour rule accepted any bright, nearly colourless surface.
  The user graded these "bad" rather than "no real sky", so the report's false-sky line (0 of 20) understates them.
- **Measured on those photos** (each connected part of the mask, at analysis size): real sky had a median local
  texture (standard deviation of brightness in a 5 × 5 window) of 0.0009–0.0058 and a mean brightness of at least
  0.59. Walls and panels had a texture of 0.0076–0.0193, or a brightness of 0.32–0.49.

### Sky rules version 2
- After the sky has grown, each connected part is kept only if its median texture is at most **0.006** and its mean
  brightness at least **0.5**. A part that contains an Include click is always kept.
- On round 1's photos this removed the wall or panel on all 8, and left the good masks unchanged. The one loss:
  IMG_1035's real sky was a 0.2 % sliver, and it is now reported as "No sky found".
- `RulesVersion` is 2, so cached sky masks from version 1 are recomputed.
- **Unit tests:** a pale textured wall or a shaded blue panel at the top edge is not sky; sky beside a wall keeps the
  sky and drops the wall; an Include click keeps a part the rules would drop.

### Sky, round 2 (`sky-rules@2`, 30 fresh photos, seed 2, none from round 1)
- **Found (20):** good 15, usable 5, bad 0, no real sky 0; good or usable **100 %** (95 % interval 84–100 %).
- **Not found (10):** all right.
- **Caveats:**
  - One person graded both rounds, so the bar may have moved between them.
  - The photos come from one set of cricket photos.
  - Ten "not found" photos are too few to show how often v2 now misses small patches of real sky, which round 1
    suggests it sometimes does (IMG_1035).
  - These are not rates for a whole library.

### Hand-drawn masks (`draw-sheet`, 4 of 12 photos drawn)
- The user outlined the subject on 4 photos without seeing the app's masks. One (d003) was left out of the scores:
  every shape on it had been drawn in Cut mode, so it measured nothing. The page now makes a layer's first shape Add.
- **Subject, 3 photos:**
  - 96–100 % of the app's mask lies inside the user's outline (median 99 %), and the app picked the same people on
    all 3.
  - IoU was 0.58–0.78. The low figure comes from loose outlines drawn around the people, not from the app's masks.
  - Edge agreement was about 0.16 within 3 px, low for the same reason.
- **Sky, 1 photo:** IoU 0.14 (rules version 1). One photo says nothing general.
- **Limit:** 3 photos is a spot check, not a measurement. The brief's "mask IoU against hand-drawn masks" is partly
  met; a real IoU figure would need a larger set with tighter outlines.
