# Milestone 03 — Culling Workflow

## Goal
Allow photographers to review, mark, filter, compare, and export photo selections.

## Deliverables
- Keep / Reject / Review / Unset decisions
- Rating and colour label
- Persist decisions
- Filter and sort controls
- Keyboard shortcuts with visible help
- Compare two or more photos (start with side-by-side)
- Export selected photos to a destination OR export a selection manifest
- Safe collision handling and clear export progress

## Acceptance criteria
- Decisions survive application restart
- Filtering and sorting are deterministic
- Keyboard workflow works in grid and viewer
- Export never modifies source files
- Duplicate filename collisions are handled safely
- Tests cover decision rules and export behavior

## Prompt
Implement only Milestone 03. Read the existing code and propose the plan. Prioritise a fast keyboard-driven culling workflow. Keep selection, focus, and culling decision distinct. Add undo for decision changes if it fits cleanly. Build, test, and update docs. Do not implement AI scoring.
