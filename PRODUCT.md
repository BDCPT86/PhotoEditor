# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

> FrameForge is a native **Windows desktop app (WPF, .NET 10)**, not a website. "web" is recorded only because the
> schema has no desktop value; it means no iOS/Android design language applies. Follow Windows desktop conventions
> (keyboard-first, Windows scaling, standard focus behaviour).

## Users
The primary user is the developer themself: a photographer (club cricket and similar outdoor sports) who culls and
edits their own shoots, typically hundreds to about a thousand 24 MP Canon JPEGs per session, for delivery or
sharing. Selling to other photographers is not a current goal.

## Product Purpose
A local-first tool to get from a card full of photos to a finished, edited selection quickly: import, cull with the
keyboard, use explainable analysis and optional local AI as hints, then edit non-destructively and export. Success
means a shoot is culled and given a consistent look in one sitting, without the original files ever changing.

## Positioning
Everything runs on the user's own machine, with no cloud. Every automatic judgement explains itself (numbers,
thresholds, reasons) and stays a suggestion the user applies or rejects. Originals are never modified.

## Operating Context
- **Workflow:** import a folder → cull (Keep/Reject/Review, stars, labels) → analyse (sharpness, exposure,
  duplicates, optional faces and scenes) → edit in Develop → export copies or edited JPEGs.
- **Batch and single work equally:** automatic tools are used both on a whole selection (a first pass via presets)
  and per photo in Develop.
- **When an automatic tool runs** (Auto settings, straighten, crop), the user prefers to see **a few alternative
  results to choose from** rather than a single applied answer.
- **Typical content:** outdoor sports in harsh sun: white kit, often clipped highlights, small distant players,
  spectators in the background, helmets and caps hiding faces.

## Capabilities and Constraints
- **Built (Milestones 1–6):** import and viewer; culling, filters and compare; copy or manifest export;
  deterministic analysis; optional local AI (YuNet faces, DINOv2 scenes; consent-gated, free licences); non-destructive
  Develop (exposure, white balance, tone, colour, crop and straighten), presets with batch undo, edited-JPEG export.
- **Constraints:**
  - JPEG only (8-bit); RAW is planned (Milestone 7)
  - CPU inference; no fees or restrictive licences for any model
  - nothing is downloaded without consent
  - the app must work with no models installed
- **Terminology:** "Keep / Reject / Review", "Develop", "recipe" (internal), "preset", "Analyze", "AI pick" (advisory).
- **Undecided:** whether FrameForge will ever be distributed to others.

## Evidence on Hand
- A real test set exists locally: 1,010 of the user's cricket photos. It is used only on this machine and never
  stored in the repository.
- No human-labelled evaluation set exists yet, so no quality claims are made.

## Product Principles
1. **The photograph is the hero.** Controls stay quiet and secondary.
2. **Suggest, never impose.** Automatic results are previews or options the user picks; everything is undoable.
3. **Explain every judgement** in plain numbers and reasons.
4. **Originals are sacred:** read-only, always.
5. **Fast in bulk, precise per photo.** Every tool works on a selection and on a single photo.

## Accessibility & Inclusion
Keyboard-first operation, visible focus, tooltips on icon-only controls, meaning never carried by colour alone, and
layouts usable at common laptop resolutions and Windows scaling (from `docs/ui-design-system.md`).
