# 0016 — Moved folders and offline drives

**Date:** 2026-11-24
**Status:** Accepted (Milestone 07, phase C)

## Context
Photos are matched by their full path. Decisions, ratings, edits and analysis are all stored against each photo's id,
not its path. Before this phase:
- A photo was marked Missing only when its folder was imported again and the file wasn't there. Nothing told a deleted
  file apart from an unplugged drive.
- Moving a folder and importing it again created new photos at the new paths. The decisions and edits stayed on the old
  entries, now Missing, with no way to bring them across.
- An import that found nothing at all (an empty network share, a card reader with no card) marked every photo under
  that folder Missing.

## Decision
- **Offline is worked out at run time, not stored** (`FolderAvailabilityChecker`).
  - It runs after the library loads and whenever the view is refreshed (F5 or "Check again").
  - Each drive or network share is probed once, with a 3-second limit; a drive that doesn't answer counts as
    unavailable. Then each folder on a drive that is there is probed.
  - **What the user sees:** a photo in an unreachable folder shows **Offline**, with the reason ("Drive E: isn't
    connected.", "This folder isn't there any more…"). It counts under Problems. Its folder in the sidebar gets a warning
    mark, and a banner above the grid explains. Thumbnails already made still show, and decisions can still be made.
  - The viewer says why the photo can't be opened, rather than "This file no longer exists".
  - No database change: the moment the drive is back, the photos are back.
- **Import never marks a whole folder Missing at once.** If a scan finds no photos where the library has some, nothing
  is marked Missing (`ImportSummary.MissingCheckSkipped`). The result says so and the log has a warning.
- **Locate folder…** (`FolderRelocationService`, `FolderRelocationPlanner`):
  - **Where:** from a sidebar folder's right-click menu, or the banner. The user chooses where the folder is now.
  - **Matching:** each photo under the old folder is matched to `new folder + its path relative to the old folder`, and
    must have the same size. The preview lists found, found with a different date (moved too), different size, not
    found, and already in the library (left as they are).
  - **Applying:** one `SaveChanges`, so all moves happen or none do (the unique path index would roll a clash back).
    Ids are kept, so decisions, edits, presets applied and analysis stay. A Missing photo that is found becomes
    Imported. No file on disk is touched.
  - Thumbnails and masks are keyed partly by path, so they are made again. Analysis is keyed by id, size and modified
    time, so it stays valid unless the modified time changed.

## Alternatives
- **Storing an Offline status in the database:** rejected. It would go stale the moment a drive is plugged in, and
  needs a migration for something that can be checked in milliseconds.
- **Matching moved photos by content hash:** more robust against renames, but it reads every file in full (about 8 MB
  each). Relative path plus size is instant and matches how folders are actually moved or copied. A hash check could be
  added for the "different size" and "not found" cases later.
- **Searching all drives for a moved folder automatically:** slow, surprising, and could pick the wrong copy. The user
  knows where they moved it.
- **Merging with photos already imported at the new place:** deferred. It needs a rule for whose decisions and edits
  win. Those photos are listed and left as they are.

## Consequences
- Deleting every photo in a folder outside FrameForge and importing it again no longer marks them Missing. They show
  Offline if the folder itself is gone, and otherwise stay as they were. This is the price of not losing work to an
  empty share.
- A file renamed inside the moved folder is reported "not in the new folder". Importing the new folder adds it as a
  new photo.
- With one offline network share on the library, each library load or refresh waits up to 3 seconds for it, off the
  UI thread. The grid shows at once with the last known state.
