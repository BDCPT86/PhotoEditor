# Milestone 08 — AI-Assisted Editing

> **Status (2026-11-17):** Phase A (the parts with no new model) is implemented: Auto light, Auto level, Auto crop to
> faces, batch Auto in the Presets panel with an Auto-applied filter, and zoom to faces. See
> [ADR 0007](../decisions/0007-auto-editing-tools.md). Phase B (recipe v2, masks, model choices) is still a draft and
> needs approval. Round 1 of the two human evaluations (a blind A/B of Auto light, and Auto crop compared with the
> user's own crops) is done: the user preferred the original over Auto light and cropped much looser than Auto, so both
> were retuned. In round 2, on fresh photos, the crop size matched, and Auto light no longer lost clearly (5 to 11,
> p = 0.21). See [evaluation-m08.md](../evaluation-m08.md).
>
> **Phase B (approved 2026-11-18):** B1 model spike → B2 recipe v2 → B3 mask pipeline → B4 renderer → B5 Develop UI →
> B6 batch → B7 evaluation. Subject: MobileSAM (prompted by the M05 faces) against MODNet; Sky: no model first;
> Background: the inverse of Subject; click-to-select, first planned for later, moved into phase B after B1. B1 round 1 is done: both licences are verified at pinned
> revisions, and in the blind grading neither model was good enough (good or usable: MobileSAM 37 %, MODNet 47 %),
> mostly because faces do not say who the subject is. MODNet is rejected. In round 2, YOLOX-s finds the people
> and MobileSAM masks them: 27 of 30 masks were usable (90 %), none good, usually because bats, hands or feet were
> cut off. This combination is accepted (B1 done), with box padding and click-to-select to fix missing parts. See
> [ADR 0008](../decisions/0008-subject-mask-model.md). B2 is done: recipe schema v2 adds local adjustments (mask
> references plus settings), still writes version 1 when there are none, and presets get a Masks group; see
> [ADR 0009](../decisions/0009-recipe-schema-v2.md). B3 is implemented: the mask service, the subject mask (YOLOX-s
> people → MobileSAM, refined by clicks), a sky mask without a model, the three catalogue entries and a disk cache;
> see [ADR 0010](../decisions/0010-mask-pipeline.md). Padding the boxes was tried and rejected: in a blind round 3,
> unpadded masks were graded better on 14 of 30 photos and padded ones on none, with 29 of 30 unpadded masks good or
> usable (20 good). B4 is implemented: the renderer (`render-v2`) blends local adjustments in linear light, with masks
> that follow the geometry and a guided-filter edge; recipes without masks render exactly as before; the viewer and
> export use it. See [ADR 0011](../decisions/0011-local-adjustment-rendering.md). B5 is implemented: Develop's **Masks**
> section (add Subject, Sky or Background; per-mask sliders, invert, delete, show (O), Refine with clicks (M), progress
> with cancel, reasons when a mask is unavailable, a Masks preset group), with clicks reusing the photo's model work so
> only the decoder runs; see [ADR 0012](../decisions/0012-develop-masks-ui.md). Checked in the app (visual review M08); sky
> rules pick up bluish roofs touching the top edge, left for B7. B6 is implemented: presets with masks work them out
> photo by photo (progress, estimate, countdown, cancel, Undo last apply), leave out a mask that can't be made and list
> the photo, and are refused up front without the subject models; edited exports list masks they left out. See
> [ADR 0013](../decisions/0013-batch-masks.md). Checked in the app by the user, with the folder filter. Next: B7, evaluation and speed.

> Draft brief. Its number follows M07 (RAW and professional workflow), but it depends only on M05 (model handling) and
> M06 (the edit recipe and renderer), so it can be scheduled before M07 if preferred.

## Goal
Use local models to *suggest* and *speed up* edits, so a user gets a good starting point in one click and can
adjust selected areas (subject, sky, background) instead of the whole photo. AI never applies an edit on its own,
never overwrites the user's settings without an undoable action, and never touches the original file.

## Scope (proposed, in order of value)
1. **Auto settings (no model first).** One button fills exposure, white balance, contrast, highlights and shadows
   from the photo's histogram and grey-world/white-patch estimates. It's deterministic and explainable ("lifted
   exposure +0.4 EV because the median is at 28 %"), so a model is only added if it measurably beats this baseline.
2. **Masks for local adjustments.** "Select subject" (people), "Select sky", and "Background" (the inverse of the
   subject). Each mask can carry its own exposure, white balance, contrast, highlights/shadows and saturation.
3. **Auto-straighten (no model).** Detect the horizon or the dominant near-horizontal/vertical lines (fences, walls,
   boundary ropes) with an edge and line search, and propose a **Straighten** angle. When no reliable line is found
   it says so and leaves the angle unchanged.
4. **Auto-crop to subject.** Propose a crop at the user's chosen aspect ratio that frames the subject:
   - Subjects come from M05 face boxes (already stored) and, once available, the subject mask.
   - Framing follows explainable rules (rule of thirds, headroom, space in the direction a face looks, never cutting
     through a face) and shows its reason ("placed the main face on the upper-left third").
   - It never crops tighter than a minimum output size (default: long side ≥ 2,000 px), so distant players are not
     blown up into soft images. If a tighter crop is impossible, it says so.
5. **Batch reframe.** Presets can include "auto-straighten" and "auto-crop at aspect X". This runs per photo with
   progress, cancel and undo (the M06 batch rules); photos with no detectable subject are left uncropped and listed.
6. **Zoom to faces (viewer).** A key steps the 100 % view from one detected face to the next, to check focus quickly.
   It uses the stored M05 face boxes, so it needs no new model.
7. **Optional: learned auto-enhance, noise reduction or upscaling ("AI zoom").** Only if a model with a clear licence
   beats the non-AI baseline on a human-reviewed set. These are the heaviest and most uncertain items, and may be
   dropped. Upscaling would always be labelled as generated detail.

## Deliverables
- **Recipe schema v2** (versioned migration of the stored JSON; v1 recipes load unchanged):
  - a list of local adjustments, each with a mask *reference*: the kind, the model version tag, parameters and an
    optional inversion
  - the adjustment's settings, using the same ranges and validation as the global ones
- **Mask provider port** in Application, with ONNX implementations in `FrameForge.AI` behind M05's `IAiRuntime` and
  model store.
  - Masks are derived data: computed from the original at analysis resolution, cached on disk under the app data
    folder (keyed by photo identity, model tag and parameters), and recomputed when missing or when the model changes.
    Mask images are never stored in the database.
- **Renderer:** blend the per-mask adjustments in linear light, with a feathered, edge-aware mask upsampled to the
  output size. Preview and export stay one code path (the M06 rule), and a neutral recipe is still bit-identical.
- **Develop UI:**
  - **Auto** (undoable)
  - a **Masks** section: add Subject / Sky / Background, show the mask overlay, per-mask sliders, invert, delete
  - the progress of mask computation, with cancel; masks are computed on demand when added, not for the whole library
- **Reframing:**
  - an `IAutoStraighten` service (deterministic line detection)
  - an `IAutoCrop` service (pure framing rules over face boxes and masks)
  - Develop buttons **Auto straighten** and **Auto crop**; each sets the normal crop and straighten fields, so the
    result can be adjusted by hand and undone in one step
  - a viewer key for zoom to faces
- **Batch:** presets can include "Auto", auto-straighten, auto-crop and mask adjustments; masks are computed per photo during the batch, with
  progress, cancel and undo (the M06 batch rules).
- **Model catalogue entries:** source, pinned revision, size, SHA-256, licence and evaluation, each recorded in an ADR
  as in M05. Download only with consent; the app works fully without the models.
- **Evaluation:** extend `tools/FrameForge.Evaluation`:
  - mask IoU against hand-drawn masks for a small labelled set
  - auto-settings comparison (the user's preferred version, blind A/B)
  - latency and memory per photo

## Candidate models (licences must be re-verified at the pinned revision before approval)
| Use | Candidates to evaluate | Notes |
|---|---|---|
| Person / subject mask | MODNet, U²-Net (small), MediaPipe selfie segmentation | Reported as Apache-2.0; verify the weights' licence, not just the code's |
| Prompted / general mask | MobileSAM, SAM 2 (tiny) | Reported as Apache-2.0; heavier, so CPU latency must be measured |
| Sky mask | A semantic segmentation model with a "sky" class (e.g. a small ADE20K-trained model) | The licence of the training data and weights varies; many are research-only |
| Noise reduction (optional) | NAFNet, SCUNet | Verify the licence of the weights and the JPEG-artefact behaviour |
| Upscaling (optional) | Real-ESRGAN (small), SwinIR | Verify the weights' licence; output is generated detail and must be labelled |
| Straighten, crop, zoom to faces | No new model: line detection, framing rules, M05 YuNet boxes | Deterministic and unit-testable |

**Rejected up front:** anything non-commercial or research-only (e.g. BRIA RMBG), AGPL, or paid. This follows the
rule from M05 that models must carry no fee and no usage restriction.

## Acceptance criteria
- The app works when no editing model is installed, and Auto still works because it needs no model.
- AI suggestions are applied only by a user action, and each is a single undo step.
- Model and mask failures don't break Develop: the mask is shown as unavailable with the reason, and the rest of the
  edit still renders.
- Mask computation is cancellable, has bounded concurrency, and is cached by model version, photo identity and
  parameters.
- Preview and export stay consistent with masks (the preview matches the downscaled export, as in M06).
- Recipe v1 → v2 migration is tested, and v2 recipes round-trip exactly.
- Golden-pixel tests cover mask blending; a visual review is documented on the user's photos (kept out of the
  repository).
- Every model's source, version, licence and checksum is documented, and quality is reported against a human-reviewed
  set before any claim is made.
- Auto-straighten is tested on synthetic tilted horizons; it must not change photos with no confident line.
- Auto-crop is tested on synthetic face layouts:
  - every face stays fully inside the crop, the aspect ratio is exact, and the minimum output size is respected
  - it is compared with the user's own crops on a labelled sample (agreement reported, with no quality claim)
- Batch reframing reports the photos it skipped, and why.
- Latency and memory are reported on the target machine (goal: a subject mask in under 1 s on CPU for a 24 MP photo,
  computed at analysis resolution).

## Risks
- Mask quality on sports photos (motion, partially hidden players, small distant people) may be poor. Measure it
  before building UI around it.
- Getting an edge-aware feather wrong produces halos. This needs a guided-filter or similar refinement step, and
  visual review.
- The recipe schema change affects stored data and presets. Migration must be reversible and tested.
- CPU-only inference may be slow for SAM-class models; prefer small models, or offer them only as optional.
- **Auto-crop on sports photos:**
  - Several players, a subject turning away (no face), or a ball in play make "the subject" ambiguous. The face
    boxes include spectators, so it should frame the largest main faces (the M05 main-faces rule), and say when it
    is unsure.
  - Cropping cannot add resolution. Tight crops of distant players will be soft unless optional upscaling is
    approved.

## Out of scope
Generative fill or object removal, face retouching, style transfer, cloud processing, and training or fine-tuning
models.

## Prompt
Implement only Milestone 08 after proposing and getting approval for the model choices (no fees, verified weight
licences). Start with the parts that need no new model: Auto settings, auto-straighten, auto-crop from face boxes,
and zoom to faces. Then do the recipe v2 schema, then one mask type end to end before the others. Keep model code behind interfaces, never download silently, keep AI output as user-applied, undoable
suggestions, and never write to originals. Build, test, benchmark, evaluate on a human-reviewed set, and document
the limitations.
