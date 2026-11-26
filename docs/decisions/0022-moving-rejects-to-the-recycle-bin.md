# 0022 — Moving rejects to the Recycle Bin

**Date:** 2026-11-26
**Status:** Accepted (Milestone 09, phase B)

## Context
Culling ends with getting rid of the rejects, which until now meant finding them by hand in File Explorer. FrameForge
had promised never to move or delete a photo. Any way of removing files has to keep the spirit of that promise:
nothing is lost by accident, and nothing happens without the user's say-so.

## Decision
- **The Recycle Bin only, never a permanent delete** (`IRecycleBin` in Application, `RecycleBinService` and
  `WindowsRecycleBinPlatform` in Infrastructure).
- **Why three layers of protection:** Microsoft's documentation doesn't say what the shell does with a file it can't
  recycle when told not to ask. So the design doesn't rely on the shell getting it right:
  1. **Before each file**, checked again just before it moves: only local fixed drives qualify (drive letter,
     `DriveType.Fixed`, ready). The drive's Recycle Bin settings must exist (`BitBucket\Volume\{guid}`), its
     `NukeOnDelete` must be off and the `NoRecycleFiles` policy unset, and the file must be smaller than the bin's
     `MaxCapacity`. Network shares, removable drives and SUBST drives are refused, never deleted.
  2. **The move:** the shell's file operation, one file at a time, with allow-undo and no confirmation. It also asks for
     the "will be deleted permanently" warning (`FOF_WANTNUKEWARNING`), which Microsoft documents as overriding "no
     confirmation".
  3. **After each file:** the file must be gone and the drive's Recycle Bin must hold one more item. If not, moving
     stops and the rest are not tried.
- **The command:** "Move to Recycle Bin…" beside the heading of the Rejected view, and the **Delete** key. It acts on
  the rejects shown, so a folder or filter narrows them, and takes both files of each pair. It opens a side panel that
  only checks the files:
  - photos, files and total size;
  - the folders they come from;
  - what will be skipped, and why.

  Nothing moves until "Move N files to Recycle Bin" is pressed. Focus starts on Cancel, so Esc closes the panel and a
  stray Enter can't move anything.
- **Afterwards:** the photos whose files went leave the library (`IPhotoRepository.RemoveAsync`), with their
  decisions, edits and analysis through the existing cascading foreign keys. A file that wasn't moved keeps its photo.
  Undo forgets only the steps that touched removed photos. The status line says what moved and why the rest stayed,
  and the log has one line per file.
- **Getting a file back:** restore it from the Recycle Bin and import its folder again. It returns as a new photo,
  without its Reject mark. There is no in-app undo for the move.
- **The promise is now:** "FrameForge never changes your photos. It moves rejects to the Recycle Bin only when you ask,
  and only after you confirm." It appears in About, the README and the user guide.

## Alternatives
- **Offering a permanent delete for drives without a Recycle Bin,** after a second confirmation: rejected. A file
  deleted from a memory card can't be got back, and the user can delete it in File Explorer if they mean to.
- **Keeping moved photos in the library as Missing,** with their decisions: rejected. The library would fill with
  photos that are gone, and the Rejected view would never empty.
- **Moving all files in one shell call:** rejected. It wouldn't show which file failed, and a problem with one file
  could affect the rest.
- **`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(…, RecycleOption.SendToRecycleBin)`:** rejected. It doesn't
  ask for the permanent-delete warning, so a file that can't be recycled could be deleted silently.

## Consequences
- Rejects can be cleared from inside FrameForge, safely, with a full record in the log.
- **Not verified on real Windows:** whether the permanent-delete warning appears for a file that slipped past the
  checks, in silent mode. The checks keep such files from reaching the shell, and the count check stops moving if one
  vanishes.
- **Possible false stop:** when the Recycle Bin is nearly full, Windows removes its oldest items to make room. The
  count may then not rise, and moving stops on the safe side.
- **Infrastructure** now allows unsafe code, for the source-generated P/Invoke calls (as FrameForge.Raw does).
