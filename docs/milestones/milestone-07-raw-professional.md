# Milestone 07 — RAW and Professional Workflow

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
