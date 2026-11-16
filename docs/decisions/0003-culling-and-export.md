# 0003 — Culling decisions, keyboard model, and safe export

**Date:** 2026-11-16
**Status:** Accepted (Milestone 03)

## Context
Milestone 03 adds Keep/Reject/Review/Unset decisions, ratings, labels, filters, compare and export, with a fast
keyboard workflow. The brief requires selection, focus and decisions to stay distinct, deterministic filtering,
safe handling of file-name collisions, and that export never modifies sources.

## Decision
- **Decisions are applied optimistically:** the UI updates first and saves in the background. Saves are serialised,
  and a failed save reverts the change and removes it from undo history.
- **Undo/redo** works at the level of a user action (one key press may change many photos), capped at 200 entries
  and kept in memory only.
- **One keyboard table** (`CullingShortcuts`) drives key handling and the help panel. Plain letters and digits are
  handled by an attached behaviour, since WPF `KeyGesture` does not support them without a modifier.
- **Linear ← / → navigation in the grid** via a `ListBox` subclass, replacing WPF's within-row spatial navigation.
- **Filters prune in place, and the viewer/compare use snapshots:** filtering on Unset works as a queue without the
  view jumping, and the order only changes on an explicit refresh.
- **Export never overwrites:** names are planned up front, then each file is created with `CreateNew` under a temporary
  name and moved without overwrite, retrying with the next free name. The manifest is CSV, with formula-injection
  escaping.
- **No new dependencies.**

## Alternatives
- *Save first, then update the UI:* simpler consistency, but a visible delay on every key press during culling.
- *Persist undo history:* survives restarts, but adds a table and raises questions about undoing across imports.
  Not needed yet.
- *Re-sort live after every change:* photos would jump while being rated. Rejected for a culling workflow.
- *Overwrite or skip on collision:* overwriting risks data loss; skipping silently drops photos. Suffixing keeps everything.
- *Manifest as JSON:* better for tools, worse for photographers. CSV opens in any spreadsheet; JSON can be added later.

## Consequences
- After a rating change under the Rating sort, the order is stale until F5, a filter or sort change, or closing the
  viewer or compare. This is documented in the help panel and README.
- Undo history is lost on restart. The decisions themselves persist.
- Export into an imported folder is allowed after confirmation; those copies will be imported as new photos.
