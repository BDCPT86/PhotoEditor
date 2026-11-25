# Milestone 07 — RAW and Professional Workflow

> **Status (2026-11-24):** In progress, in phases, each approved on its own. Milestone 08 was done first, because it
> depends only on M05 and M06.
>
> - **Phase A (RAW), paused.** The A1 spike tool is built: `raw-spike` in `tools/FrameForge.Evaluation`, using LibRaw
>   0.22.3, the first version that lists the Canon EOS R50. It has not run, because no CR3 files are available yet.
>   Sdcb.LibRaw was dropped: its native package is LibRaw 0.21.1, with no R50 support and from before the 0.22.1 security
>   fixes.
> - **Phase C (moved folders, offline drives), implemented.**
>   - Photos on a drive or folder that can't be reached show as Offline, with the reason, without a database change.
>   - An import that finds nothing never marks a folder Missing.
>   - **Locate folder…** points a moved folder's photos at the new place, in one transaction, keeping decisions and
>     edits.
>
>   See [ADR 0016](../decisions/0016-moved-folders-and-offline-drives.md).
> - **Order planned for the rest:**
>   - B: export profiles and naming
>   - D: performance and memory
>   - F: licence audit
>   - E: installer and crash-report design
>   - A: RAW, once CR3 samples exist

## Goal
Evaluate and integrate RAW decoding and harden the app for professional use.

## Deliverables
- LibRaw integration spike with supported Canon RAW sample files
- Document supported formats/camera limitations
- RAW thumbnail/preview workflow
- Metadata preservation strategy
- Export profiles and naming templates
- Relocation/missing-drive handling
- Performance profiling and memory improvements
- Installer and update strategy
- Opt-in diagnostics/crash reporting design
- Commercial dependency/model licence audit

## Acceptance criteria
- RAW files decode on supported test cameras
- Unsupported/corrupt files fail gracefully
- Source RAW files remain untouched
- Export metadata behavior is documented
- Performance/memory benchmarks are recorded
- Distribution licence checklist completed

## Prompt
Implement only Milestone 07. Begin with a technical spike and compatibility report before integrating LibRaw. Verify native library deployment, architecture, licensing, and sample camera support. Do not promise universal RAW compatibility. Add tests and document supported formats and known limitations.
