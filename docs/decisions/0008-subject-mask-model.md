# 0008 — Subject mask model for local adjustments

**Date:** 2026-11-18
**Status:** Accepted 2026-11-18 (Milestone 08 phase B, step B1): YOLOX-s + MobileSAM, after two rounds of blind
grading. Approved by the user.

## Context
Phase B adds masks for local adjustments: Subject, Sky and Background (the inverse of Subject). Sky starts without a
model. The subject mask needs one. The brief requires a model with no fee and no usage restriction, licences verified
at a pinned revision, and quality measured on a human-reviewed set before any claim. The photos are mostly sports:
whole figures, several players, often far away.

## Candidates

| | MobileSAM (prompted) | MODNet (portrait matting) |
|---|---|---|
| Upstream | `ChaoningZhang/MobileSAM` @ `f706ad9c4eb7f219c00d9050e46328518ffb65d2` | `ZHKKKe/MODNet` @ `28165a451e4610c9d77cfdf925a94610bb2810fb` |
| Licence (verified) | Apache-2.0: the repository's `LICENSE`; the weights are in the same repository | Apache-2.0: the README says "the code, models, and demos" are Apache-2.0 (except GIFs in `doc/gif`) |
| ONNX files | `nrl-ai/samexporter-onnx-models` @ `5050a79cd4b912dd745fff83047c4ef6fbd97be5`, `mobile_sam/` (Apache-2.0 `LICENSE` in the folder; `PROVENANCE.md` says it was exported from the official checkpoint at the upstream commit above) | `Xenova/modnet` @ `fa2fa546052fba4c08921230a26cc69a333fca12`, `onnx/model.onnx` (tagged Apache-2.0) |
| Files, size, SHA-256 | `mobile_sam.encoder.onnx`, 28,157,853 B, `8c1494f7dc70b61b15bc7ab8e4291804d47294d881da137b099b04c1776535dd`<br>`mobile_sam.decoder.onnx`, 16,502,895 B, `23c11087a0c1930d863ba37fdf0f2b0080f5a94269191f679ffd16b8415f0a38` | `model.onnx`, 25,888,640 B, `07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9` |
| How it is used | Encoder once per photo (RGB 0–255, long side 1024, preprocessing inside the export); decoder once per main face, prompted with a body box made from the M05 face box (corners labelled 2 and 3) plus the face centre as a positive point; masks combined | One pass, no prompt: RGB scaled to −1…1, short side 512, sides floored to ×32; outputs an alpha matte |

`Acly/MobileSAM` was not used: its MIT tag does not match the upstream Apache-2.0 licence.

**Rejected before the spike:** SegFormer (NVIDIA licence, non-commercial), BRIA RMBG (non-commercial), YOLO segmentation
(AGPL), BiRefNet (MIT weights, but trained on DIS5K, whose terms are academic; also heavy), torchvision DeepLabV3 (no
licence stated for the weights), and ADE20K-trained models for sky (the ADE20K images are for non-commercial research).

## Measurements (2026-11-18)
30 of the user's photos with a main face, chosen by a seeded random sample (seed 1), masks at 1024 px. Machine:
i7-13700H laptop on mains power in the vendor's "Balance" power mode, with Docker and Teams running; ONNX Runtime CPU,
2 threads per session (the app's setting).

| | MobileSAM | MODNet |
|---|---|---|
| Per photo, median (90th percentile) | 4.6 s (5.4 s): encoder 4.2 s, decoder 0.4 s for a median of 2 main faces | 2.0 s (2.3 s) |
| Best runs on synthetic input, 2 threads | encoder 0.7 s | 0.35 s |
| Model memory when loaded (both together) | 83 MB | |
| Median share of the photo inside the mask | 3.1 % | 4.7 % |

Timings varied by a factor of up to 4 between consecutive runs, and more threads made them slower, which points to
the laptop's power limits rather than the models. They will be measured again in a quiet state (step B7). Neither
model reached the brief's goal of under 1 s per mask in sustained runs on this machine.

**First look (three photos, not a measurement):**
- Both are clean on a single player.
- MODNet sometimes adds objects near the player (a stump, a patch of ground) and people who are not the subject (a
  passer-by).
- MobileSAM breaks up on a tight group: each face's body box overlaps the other players, so a decode catches parts of
  several people. A single box around the whole group may work better; it was not tried, to keep the grading honest.

## Quality: blind grading, round 1 (2026-11-18)
The user graded each photo's two masks, sides random, as good (use as is), usable (use after a small fix) or bad.

| | Good | Usable | Bad | Good or usable (95 % interval) |
|---|---:|---:|---:|---|
| MobileSAM, prompted from faces | 4 | 7 | 19 | 37 % (22–54 %) |
| MODNet | 4 | 10 | 16 | 47 % (30–64 %) |

Graded better: MobileSAM on 5 photos, MODNet on 7, equal on 18 (sign test p = 0.77, no real difference). Both were
bad on 15 of 30, six of them with a single main face.

**Why (from looking at the failures, not measured):** the masks were mostly wrong about *who* the subject is, not
about the outline.
- In most action photos several players are in frame but only one face is visible enough to count as a main face
  (the umpire, the keeper, the batter). MobileSAM masked that person cleanly and missed the players facing away,
  such as the bowler.
- In tight groups, each face's body box overlaps the other players, so MobileSAM catches parts of several people.
- MODNet has no prompt: it picks whatever looks like a portrait subject, including the wrong players, spectators,
  stumps and once a tree branch. It cannot be told who the subject is.

**Decision:** neither is accepted as is.
- **MODNet is rejected:** with no prompt it cannot be pointed at the subject, and it was no better.
- **MobileSAM is kept as a candidate:** where it was prompted on the right person the outline looked clean. The
  prompts have to come from the people in the photo, not from faces.

How to build and score a sheet:

```bash
# round 1: MobileSAM from faces vs MODNet
dotnet run --project tools/FrameForge.Evaluation -c Release -- mask-sheet --round 1 --models <dir> --photos <folder> --sheet <new dir> --count 30 --seed 1
# round 2 (default): people → MobileSAM, skipping the photos of an earlier sheet
dotnet run --project tools/FrameForge.Evaluation -c Release -- mask-sheet --models <dir> --photos <folder> --sheet <new dir> --count 30 --seed 2 --exclude <round 1 sheet dir>
dotnet run --project tools/FrameForge.Evaluation -c Release -- score-sheet --sheet <dir> --answers <saved csv>
```

The answers and the round 1 report are kept outside the repository with the user's photos.

## Round 2: people found by a detector prompt MobileSAM (approved 2026-11-18)
- **Person detector:** YOLOX-s from OpenCV Zoo.
  - Source: `opencv/opencv_zoo` @ `26cc381e4d2594bb9f47a26eb8fd96c94a13660d`, the same pinned commit as YuNet.
  - File: `models/object_detection_yolox/object_detection_yolox_2022nov.onnx`, 35,858,002 B, SHA-256
    `c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063`.
  - Licence: Apache-2.0 (the folder's `LICENSE`). It is trained on COCO, like most detectors: the COCO annotations
    are CC BY 4.0 and the images keep their Flickr terms, the same caveat accepted for DINOv2 in ADR 0005.
  - Used as in OpenCV Zoo's demo: RGB 0–255, scaled into 640 × 640 at the top-left, padded with grey 114; score =
    objectness × person; non-maximum suppression at IoU 0.5; threshold 0.5.
- **Main people:** at least half the height of the tallest person found (the same idea as main faces), so distant
  fielders and spectators are left out.
- **Prompt:** each main person's box (corners only), one decode each, masks combined.
- **Sample:** 30 fresh photos (seed 2), none of them from round 1; every photo had at least one person.
- **Speed** (same machine and conditions as round 1): detector 0.7 s, MobileSAM encoder 3.3 s, decoder 0.5 s for a
  median of 3 main people; total 4.6 s per photo (90th percentile 5.2 s). The two models add 95 MB when loaded.
- **First look (four photos, not a measurement):** players facing away, bowlers and keepers are now found, with bats;
  the outlines are clean. Everyone of similar size counts as a subject, including an umpire.

### Round 2 grading (2026-11-18)

| | Good | Usable | Bad | Good or usable (95 % interval) |
|---|---:|---:|---:|---|
| Round 1: MobileSAM from faces | 4 | 7 | 19 | 37 % (22–54 %) |
| **Round 2: people → MobileSAM** | 0 | 27 | 3 | **90 % (74–97 %)** |

- Several main people: good or usable on 23 of 25; one main person: 4 of 5.
- **The usual fix (the user's answer):** parts missing, such as bats, hands, feet and caps. Nothing was graded good
  for that reason.
- **The three bad masks (looked at, not measured):** a batter's bat mostly left out; a crouching fielder left out and
  two further players included; a tangle of keeper, slip and batter.
- **Likely causes:** SAM keeps the mask inside the box it is given, and YOLOX's box hugs the body, so a bat or a
  raised arm outside the box is cut off; and SAM's 256 × 256 logits lose thin parts such as a bat handle.

## Decision (accepted 2026-11-18)
Use **YOLOX-s to find the main people and MobileSAM to mask them** for the subject mask. It clears the bar (most masks
good or usable) on fresh photos. MODNet is rejected.

Before the subject mask counts as done, the following are required:
- **Missing parts:** pad each person's box (more at the top and sides, for bats and raised arms) and check thin parts
  at the mask's resolution. This is done in B3 and checked with a fresh round 3 sheet, not tuned on rounds 1–2.
  *(B3: tried and rejected. In the blind round 3 grading, unpadded boxes were better on 14 of 30 photos and padded
  ones on none, so boxes are used as detected and missing parts are left to clicks. See
  [ADR 0010](0010-mask-pipeline.md).)*
- **Click-to-select in phase B:** a click adds a positive point (include this bat) or a negative one (not this
  umpire) to the same SAM prompt. This fixes the wrong-people cases, and SAM supports it directly.
- **Speed:** 4.6 s per photo on this laptop in sustained runs is over the brief's goal of under 1 s. Masks are
  computed on demand and cached, so Develop pays this once per photo with progress shown; batch runs are slow. It
  is measured again in B7 in a quiet state, and a GPU provider remains deferred (ADR 0005).

## Consequences (whichever model is chosen)
- The model is optional and downloaded only with consent, as in M05. Subject masks need it, and Develop says so when
  it is missing; Sky (no model) and the rest of Develop work without it.
- Masks are computed on demand and cached by photo identity, model version and parameters, so the time above is paid
  once per photo.
