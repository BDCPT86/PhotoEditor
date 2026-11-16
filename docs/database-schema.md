# Initial Database Schema

This is a starting proposal. Keep it lean and evolve through migrations.

## AppSettings
_Implemented in Milestone 01 (migration `InitialCreate`). `Key` max length 200; `ValueJson` is the JSON-serialised value (enums by name)._
- `Key` (string, primary key)
- `ValueJson` (text)
- `UpdatedUtc` (datetime)

## Photo
_Implemented in Milestone 02 as table `Photos` (migration `AddPhotos`). Differences from the proposal below:_
- _`FolderPath` (string, NOCASE, indexed) added, for scoping imports and missing-file detection._
- _`FilePath` uses NOCASE collation; its unique index is case-insensitive, matching Windows paths._
- _`CaptureUtc` became `CaptureLocalTime`: EXIF DateTimeOriginal has no reliable time zone, so it is stored as the camera's wall-clock time rather than claiming UTC._
- _`FNumber` and `FocalLengthMm` are `double` (SQLite REAL); EF Core stores `decimal` as TEXT on SQLite, which cannot be compared or sorted in SQL._
- _`ImportStatus` is stored as a string: `Imported`, `Failed`, or `Missing`._

- `Id` (Guid, primary key)
- `FilePath` (string, indexed; unique within current path strategy)
- `FileName` (string)
- `FileSizeBytes` (long)
- `LastModifiedUtc` (datetime)
- `ImportedUtc` (datetime)
- `Width` (int, nullable)
- `Height` (int, nullable)
- `CaptureUtc` (datetime, nullable)
- `CameraMake` (string, nullable)
- `CameraModel` (string, nullable)
- `LensModel` (string, nullable)
- `Iso` (int, nullable)
- `ExposureTime` (string, nullable)
- `FNumber` (decimal, nullable)
- `FocalLengthMm` (decimal, nullable)
- `Orientation` (int, nullable)
- `ImportStatus` (enum/string)
- `LastError` (string, nullable)

## PhotoDecision
_Implemented in Milestone 03 as table `PhotoDecisions` (migration `AddPhotoDecisions`). Differences from the proposal below:_
- _`Selection` is named `State` (enum `CullState`: Unset, Keep, Review, Reject, stored as a string), so the persisted culling verdict is not confused with UI selection._
- _`Label` (enum `ColourLabel`: None, Red, Yellow, Green, Blue, stored as a string) is not nullable; `None` means no label._
- _`Rating` has a check constraint `NULL or 1–5`. Only photos with a non-empty decision have a row; an empty decision deletes it._
- _Foreign key to `Photos.Id` with cascade delete. An index on `State` supports state filters._
- _`Notes` is not implemented (no UI yet)._

- `PhotoId` (Guid, primary key / foreign key)
- `Selection` (Keep / Reject / Review / Unset)
- `Rating` (int, nullable; validate range)
- `ColourLabel` (enum/string, nullable)
- `Notes` (string, nullable)
- `UpdatedUtc` (datetime)

## Thumbnail
_Not implemented (Milestone 02). Thumbnails are found by a computed key (`ThumbnailKey`: path, size, modified time, size limit, generator version), so no table is needed. Revisit if cache size accounting or cleanup needs it._
- `PhotoId` (Guid, foreign key)
- `CacheKey` (string)
- `RelativeCachePath` (string)
- `Width` (int)
- `Height` (int)
- `GeneratedUtc` (datetime)
- `DecoderVersion` (string)

## AnalysisResult (later phase)
- `Id` (Guid, primary key)
- `PhotoId` (Guid, foreign key)
- `AnalysisType` (string)
- `ModelOrAlgorithmVersion` (string)
- `SettingsHash` (string)
- `Score` (double, nullable)
- `ResultJson` (text)
- `CreatedUtc` (datetime)

## DuplicateGroup (later phase)
- `Id` (Guid, primary key)
- `AlgorithmVersion` (string)
- `CreatedUtc` (datetime)

## DuplicateGroupMember (later phase)
- `GroupId` (Guid, foreign key)
- `PhotoId` (Guid, foreign key)
- `Similarity` (double, nullable)

## Notes
- Use UTC for stored timestamps.
- Store paths, not image bytes.
- Use migrations.
- Define delete behavior explicitly.
- Consider path changes, removable drives, and duplicate files before finalising identity strategy.
- Do not create future-phase tables prematurely unless needed.
