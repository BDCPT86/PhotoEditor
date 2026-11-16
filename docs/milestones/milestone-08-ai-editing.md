# Milestone 08 — AI-Assisted Editing (draft, not approved)

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
