# Initial Database Schema

This is a starting proposal. Keep it lean and evolve through migrations.

## AppSettings
_Implemented in Milestone 01 (migration `InitialCreate`). `Key` max length 200; `ValueJson` is the JSON-serialised value (enums by name)._
- `Key` (string, primary key)
- `ValueJson` (text)
- `UpdatedUtc` (datetime)

## Photo
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
- `PhotoId` (Guid, primary key / foreign key)
- `Selection` (Keep / Reject / Review / Unset)
- `Rating` (int, nullable; validate range)
- `ColourLabel` (enum/string, nullable)
- `Notes` (string, nullable)
- `UpdatedUtc` (datetime)

## Thumbnail
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
