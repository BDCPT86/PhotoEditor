# 0009 — Recipe schema version 2: local adjustments

**Date:** 2026-11-18
**Status:** Accepted (Milestone 08 phase B, step B2)

## Context
Masks for local adjustments (Subject, Sky, Background) need a place in the edit recipe. The brief asks for a versioned
migration in which version 1 recipes load unchanged, the change is reversible, and version 2 recipes round-trip exactly.
Recipes are stored as JSON in `PhotoEdits` and `EditPresets` (ADR 0006).

## Decision
- **New field:** `EditRecipe.LocalAdjustments`, an ordered list of at most 8 `LocalAdjustment`s. Each has:
  - a stable `Id`
  - a `MaskReference`: `Kind` (Subject or Sky), `Source` (what made the mask, with its version), `Invert`
    (Background is Subject inverted), and `Points` (clicks that add to or remove from the mask, normalised to the
    upright photo before crop)
  - the same light and colour sliders as the global ones (exposure, temperature, tint, contrast, highlights, shadows,
    saturation, vibrance), with the same ranges and validation
- **References only.** The mask itself is derived data: computed from the original and cached on disk by photo
  identity, source and parameters (step B3). It is never stored in the recipe or the database.
- **Version written = oldest that fits.** A recipe without local adjustments is written as version 1, byte for byte the
  form Milestone 06 wrote, including the computed properties that form contained; the newer computed properties and
  empty lists are left out. Earlier versions of the app can therefore still read every recipe that has no masks, and
  removing the last mask goes back to version 1. The `SchemaVersion` column records the stored version.
- **Reading:** versions 1 and 2 are accepted; in memory every recipe is upgraded to the current version (a version 1
  recipe has no local adjustments). Later versions are refused with a reason, never half-read. A version 1 recipe that
  contains local adjustments is refused.
- **Value equality:** the list is a `ValueList<T>` compared by its items, so two recipes with the same masks are
  equal, which undo, save and the preset logic rely on.
- **Neutral:** a recipe with a mask is not neutral even while the mask's sliders are all 0, so an added mask is kept
  (and stored) until the user removes it.
- **Presets:** a new `Masks` group replaces the photo's local adjustments with the preset's. Clicks are not carried over,
  because they belong to the photo they were made on. `All` is stored by name and keeps meaning the five Milestone 06
  groups, so an older "All" preset never replaces a photo's masks.
- **Stable JSON:** mask kinds are written as names (`"Subject"`), not numbers; an unknown kind or a missing required
  field refuses the recipe.

## Alternatives
- **A separate table for local adjustments:** rejected. A recipe is one validated value with one undo history, and
  splitting it would need a database migration and joins for every load, with no benefit at this size.
- **Always writing version 2:** rejected. It would make every edited photo unreadable to earlier versions after one
  save, which breaks the brief's "reversible" requirement.
- **Storing the mask with the recipe:** rejected by the brief. Masks are large, depend on the model, and can be
  recomputed.

## Consequences
- No database migration was needed; only the JSON changed. Tests pin the version 1 output against text captured from
  the Milestone 06 code.
- Until the renderer supports masks (step B4), local adjustments are stored and validated but not drawn. Nothing in
  the app creates them before Develop's Masks section exists (step B5).
- An earlier version of the app refuses a version 2 recipe as an unsupported version and lists it as unreadable (the
  Milestone 06 validation does this for any later version). A preset that includes the `Masks` group is refused the
  same way, because its group name is unknown there. What an earlier version does with such a photo afterwards was
  not tested.
