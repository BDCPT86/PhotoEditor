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

## AnalysisResult
_Implemented in Milestone 04 as table `AnalysisResults` (migration `AddAnalysisResults`). Differences from the proposal below:_
- _Key is (`PhotoId`, `Kind`) — one current result per photo and kind (`ContentHash`, `Sharpness`, `Exposure`, `PerceptualHash`), replaced when recomputed. No separate `Id`._
- _`SourceSizeBytes` and `SourceModifiedUtc` record the file identity the result was computed from; together with `AlgorithmVersion` and `SettingsHash` they decide whether the cached value is still valid._
- _No separate `Score` column: values are typed JSON in `ValueJson` (e.g. sharpness score, cell scores and peak cell; exposure percentages and histogram; hash values)._
- _Cascade delete with the photo._

- `Id` (Guid, primary key)
- `PhotoId` (Guid, foreign key)
- `AnalysisType` (string)
- `ModelOrAlgorithmVersion` (string)
- `SettingsHash` (string)
- `Score` (double, nullable)
- `ResultJson` (text)
- `CreatedUtc` (datetime)

## DuplicateGroup / DuplicateGroupMember
_Not implemented (Milestone 04): groups are derived in memory from cached hashes and the current thresholds (about 1 s for 22,000 photos), so they change instantly when thresholds change and cannot go stale. Revisit if user corrections to groups (e.g. "not similar") need to be stored._

## DuplicateGroup (proposal)
- `Id` (Guid, primary key)
- `AlgorithmVersion` (string)
- `CreatedUtc` (datetime)

## DuplicateGroupMember (later phase)
- `GroupId` (Guid, foreign key)
- `PhotoId` (Guid, foreign key)
- `Similarity` (double, nullable)

## PhotoEdits
_Implemented in Milestone 06 (migration `AddPhotoEditsAndPresets`). One row per edited photo; a neutral recipe has no row._
- `PhotoId` (Guid, primary key / foreign key to `Photos`, cascade delete)
- `SchemaVersion` (int) — the stored recipe format: 1, or 2 when the recipe has local adjustments (Milestone 08)
- `RecipeJson` (text) — validated `EditRecipe` (exposure, white balance, tone, colour, quarter turns, straighten, crop;
  from version 2 also local adjustments, each a mask *reference* plus its settings). Masks themselves are never stored
  here. Version 2 needed no database migration: only the JSON changed. See
  [ADR 0009](decisions/0009-recipe-schema-v2.md).
- `UpdatedUtc` (datetime)

## EditPresets
_Implemented in Milestone 06._
- `Id` (Guid, primary key)
- `Name` (string ≤ 80, NOCASE, unique)
- `Groups` (string) — which settings the preset applies: Exposure, WhiteBalance, Tone, Colour, Geometry, and from
  Milestone 08 Masks. `All` is stored by name and keeps meaning the first five.
- `SchemaVersion` (int), `RecipeJson` (text)
- `CreatedUtc`, `UpdatedUtc` (datetime)

## Notes
- Use UTC for stored timestamps.
- Store paths, not image bytes.
- Use migrations.
- Define delete behavior explicitly.
- Consider path changes, removable drives, and duplicate files before finalising identity strategy.
- Do not create future-phase tables prematurely unless needed.
