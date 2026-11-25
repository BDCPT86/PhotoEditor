# 0018 — Licences shipped with the app, and no telemetry

**Date:** 2026-11-25
**Status:** Accepted (Milestone 07, phase F)

## Context
- FrameForge promises that everything stays on the user's computer. The only network use is model downloads the user
  starts (ADR 0005).
- **ONNX Runtime's official builds have telemetry on by default.** The package's `Privacy.md` says Windows builds that
  are not made for Windows ML "use the cross-platform 1DS telemetry SDK … to send the same trace events to Microsoft's
  telemetry backend over HTTPS". It also creates a persistent device identifier.
  - FrameForge never turned this off.
  - It applied whenever an optional AI model was loaded.
- `THIRD-PARTY-NOTICES.md` linked to licences but didn't ship their texts. MIT requires its copyright and permission
  notice in all copies, and Apache-2.0 requires a copy of the licence (and any NOTICE file).
  - It also hadn't been checked against all 49 packages the app pulls in. `System.Numerics.Tensors` was missing, and
    several versions weren't stated.

## Decision

### Telemetry off, before the runtime starts (`OnnxPrivacy`)
Both documented switches are used. `OnnxAiRuntime`'s static constructor applies them once per process, so they are set
before any session is created. The evaluation tool and the tests that create sessions directly call the same method.

| Switch | Effect (per `Privacy.md`) |
|---|---|
| `ORT_DISABLE_TELEMETRY=1` in the process environment, set before `onnxruntime.dll` initialises | "prevents the uploader, events, and persistent device identifier from being created for the process lifetime" |
| `OrtEnv.Instance().DisableTelemetryEvents()` | suppresses non-essential events |

The variable is set for this process only (not machine-wide), so other software is unaffected.

### Licence texts ship with the app
- A `licenses/` folder in the repository holds the full texts and is copied next to `FrameForge.exe`.
- **Every text comes from the restored NuGet packages; nothing was downloaded:**
  - the packages' own LICENSE and THIRD-PARTY-NOTICES files, including ONNX Runtime's 344 KB `ThirdPartyNotices.txt`
    for the native library
  - for packages that carry only an SPDX expression: the standard Apache-2.0 text, and MIT with the copyright holder
    the package declares
- `licenses/README.md` says which file covers which package.
- **About** shows the notices, the folder's path and a one-line privacy statement.

### Audit and checklist
`docs/licence-checklist.md` lists every shipped and optional component, the evidence for its licence, and what later
phases (installer, RAW) must add. It is the M07 "distribution licence checklist".

## How it was checked
- **Unit test:** creating `OnnxAiRuntime` sets `ORT_DISABLE_TELEMETRY=1` for the process.
- **Network watch:** the evaluation tool's `benchmark` (YuNet and DINOv2, 100 runs each, about 5 s) and
  `mask-benchmark` (YOLOX and MobileSAM on 8 photos, about 20 s) were run while all TCP and UDP endpoints the process
  owned were recorded. **None was seen.**
- **Limits of that check:**
  - It sampled about every 0.7 s, so a very brief connection could be missed.
  - A run without the fix wasn't done for comparison: that would have deliberately sent telemetry again. The
    vendor's documentation is the evidence that it was on.

## Consequences
- AI features send nothing to Microsoft. The persistent device identifier that earlier versions may have created on a
  development machine isn't deleted. It stays wherever ONNX Runtime keeps it, unused.
- A new package version or a new dependency needs a matching update to the notices and `licenses/`. The checklist says
  how.
- If ONNX Runtime renames or drops the variable in a later version, the API switch still applies, and this ADR must be
  revisited when upgrading. The upgrade step is in the checklist.

## Alternatives
- **A build of ONNX Runtime without telemetry (`--no_telemetry`):** the most certain option, but it means building and
  maintaining the native library ourselves. Out of scope.
- **The API switch alone:** the vendor says a first initialisation event may already have been sent before it can be
  called.
- **Linking to licence texts online:** doesn't meet MIT and Apache-2.0, which require the text to ship with the
  software.
