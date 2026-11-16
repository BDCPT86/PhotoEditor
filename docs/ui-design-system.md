# UI Design System

## Product feel
Professional photography tool, not a generic admin dashboard. The photograph is the hero. UI should be quiet, fast, dense where useful, and keyboard-friendly.

## Visual direction
- Dark charcoal/slate surfaces, not pure black everywhere.
- Neutral text with clear hierarchy.
- One restrained accent colour; reserve status colours for meaning.
- Avoid gradients, glassmorphism, oversized dashboard cards, and decorative AI clichés.
- Use consistent spacing, typography, icon sizing, and focus states.
- Do not use generic AI-generated font/layout patterns.
- Keep controls visually secondary to images.

## Main workspace (target)
- Top: compact app menu, project/session name, import/export actions.
- Left: library/folder navigation and filters.
- Centre: virtualised photo grid or large image viewer.
- Right: metadata, decision, rating, and analysis panel.
- Bottom: filmstrip/status/progress where contextually useful.

This is a direction, not a mandate to implement all panels in Phase 1.

## Interaction principles
- Keyboard shortcuts for keep/reject, rating, next/previous, zoom, and compare.
- Shortcuts must be discoverable and configurable later.
- Selection and focus are distinct concepts.
- Show progress, cancellation, and error states.
- Avoid modal dialogs for routine operations.
- Confirm destructive actions; never delete original photos through culling actions.
- Provide undo for user decisions where practical.

## Accessibility and usability
- Visible keyboard focus.
- Tooltips for icon-only controls.
- Adequate contrast.
- Respect Windows scaling.
- Do not rely on colour alone to convey selection/status.
- Keep layouts usable at common laptop resolutions.
