# 0010 — Mask pipeline: making, caching and refining masks

**Date:** 2026-11-18
**Status:** Accepted (Milestone 08 phase B, step B3). The box padding is checked on round 3 (below) before the subject
mask counts as done.

## Context
Recipe version 2 (ADR 0009) stores mask *references*: a kind, the source that makes the mask, an invert flag and
clicks. Something has to turn a reference into pixels, quickly enough for Develop, without breaking it when a model is
missing or fails, and without storing masks in the database. ADR 0008 chose YOLOX-s + MobileSAM for the subject and
asked for padded boxes and click-to-select. Sky starts without a model.

## Decision
- **Ports** (Application):
  - `IMaskService` turns a photo plus a `MaskReference` into a `MaskOutcome`: the mask, or the reason there is none.
  - `IMaskGenerator`, one per kind, has a `Source` (stored in the reference and in the cache key) and an
    `UnavailableReason`.
  - `IMaskCache` keeps computed masks on disk.
  - The model ports `IPersonDetector` and `IPromptSegmenter` sit behind `IAiRuntime`, next to the Milestone 05 ones.
- **Mask image:** `MaskImage` (Rendering) holds one byte per pixel over the upright photo before crop, at analysis
  resolution (long side 1024, MobileSAM's input size), not at the output size. The renderer maps it through crop and
  straighten, refines the edge, and upsamples it (B4). Inversion (Background) is applied there too, so Subject and
  Background share one cached mask.
- **Subject** (`SubjectMaskGenerator`, Application, so its rules are tested without models):
  - YOLOX-s finds people with a score of at least 0.5. The main people are those at least half the height of the
    tallest, as in round 2.
  - Each main person's box is padded and prompts MobileSAM once. The masks are combined.
  - **Padding** is a share of the person's height, measured in pixels on both axes: 15 % on each side, 10 % above and
    3 % below. It is meant for bats, raised arms and caps, the parts round 2 cut off. The values were set before
    round 3 and are not tuned on rounds 1–2.
  - **Clicks:**
    - An Include click inside a person's box joins that person's prompt. Outside every box it prompts a region of its
      own, such as a hidden player or a ball.
    - An Exclude click inside a main person's box removes that person ("not this umpire"), unless the same box also
      has an Include click, in which case it refines that person's outline. Outside every box it refines every
      prompt.
    - Clicks in overlapping boxes belong to the smallest box.
  - The source names the rule version, the settings and each model file's version tag, taken from the catalogue so
    it is known before anything loads.
- **Sky** (`SkyMaskGenerator`, no model, deterministic):
  - **Sky colour:** fairly bright and either blue or pale grey or white (overcast), and not green.
  - **Growth:** from smooth sky-coloured pixels in the top 2 % of rows, and from Include clicks. It stops where the
    brightness steps by more than 0.035 between neighbours. Sky-coloured edge pixels (the horizon, a roof line) join
    the sky but do not grow it further.
  - **Clean-up:** enclosed holes up to 2 % of the photo (birds, wires) are filled. An Exclude click removes the
    connected part of the sky it lands on.
  - Less than 0.5 % sky is reported as "No sky found", with a hint to click.
- **Models:** YOLOX-s and the two MobileSAM files are catalogue entries with the pinned sources, sizes, SHA-256 hashes
  and licences recorded in ADR 0008. They are optional and installed only with consent; Select subject needs all
  three. The runtime checks that both MobileSAM files are installed before loading either.
- **Running:**
  - Masks are computed on demand for one photo, never for the whole library.
  - One mask runs at a time (a gate in `MaskService`), because a subject mask takes seconds of CPU and the models add
    about 95 MB.
  - Cancelling stops even a model run in progress (ONNX Runtime's `RunOptions.Terminate`). A cancelled mask is not
    cached.
- **Failures never break Develop:** a missing model, an unreadable photo, no people or sky found, or an exception from
  a model all give an unavailable outcome with the reason, and model exceptions are logged. A cache write failure
  still returns the mask.
- **Cache** (`DiskMaskCache`):
  - **Location:** `masks\<2-character shard>\<key>.mask` under the app data folder.
  - **Key:** the photo's identity (path, size and modified time), the source, the analysis size and the clicks.
  - **Format:** a small header (magic, version, size, the "what was found" text) followed by Deflate-compressed bytes.
  - **Safety:** files are written to a temporary file and moved into place. An unreadable file is deleted and
    computed again. Keys must be lower-case hex, so a key can never point outside the folder.

## Alternatives
- **The subject rules inside FrameForge.AI:** rejected. They would only be testable with the models present.
- **Storing masks in the database:** rejected (brief). They are large, derived and model-dependent.
- **Storing masks at the output size:** rejected. Every export size would need its own mask, and SAM's detail is
  256 × 256 over the photo anyway.
- **A semantic segmentation model for sky:** deferred. The licence-clean candidates found so far are trained on ADE20K,
  whose images are for non-commercial research (ADR 0008). The rules come first, as for Auto light.

## Consequences
- Until B4 nothing draws a mask, and until B5 nothing asks for one; the pipeline is reachable only from tests and the
  evaluation tool.
- Cached masks are not evicted. Each is a few tens of KB (about 0.7 MB uncompressed at 1024 px), and masks are made
  only for photos the user adds a mask to. Deleting the `masks` folder is always safe.
- A reference made by an older source is recomputed with the current one; `MaskOutcome.Source` says which one made
  it, so Develop can update the reference (B5).
- Known sky limits: orange or red sunsets, sky that does not reach the top edge, and bright smooth walls touching
  the top edge. Clicks fix most of these; a corner pixel where two edges meet can be left out until B4 feathers the
  edge.
- Speed is unchanged from round 2 (about 4.6 s per subject mask on this laptop). It is measured again in B7.

## Round 3: padded against unpadded boxes
Pending the user's grading. See [evaluation-m08.md](../evaluation-m08.md) when it is done.
