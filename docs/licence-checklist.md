# Distribution licence checklist

The Milestone 07 checklist for shipping FrameForge. The **Status** column is for the build at Milestone 07, phase F
(2026-11-25). Redo the checks marked "every release" before each build that leaves this machine. See ADR 0018.

## 1. Before every release
| # | Check | How | Status |
|---|---|---|---|
| 1.1 | Every package the app uses is in `THIRD-PARTY-NOTICES.md` at its current version | `dotnet list src/FrameForge.App package --include-transitive`, compared with the notices table | ✅ 49 packages, all listed |
| 1.2 | Every licence is one we can ship (no copyleft, no fees, no non-commercial or field-of-use terms) | the package's `<license>` in its `.nuspec` (`%USERPROFILE%\.nuget\packages\<id>\<version>\`) | ✅ MIT, Apache-2.0, or public domain (SQLite) |
| 1.3 | Full licence texts for each, and the packages' own notice files, are in `licenses/` and copied next to the exe | `licenses/README.md` index; look in `bin\…\licenses\` | ✅ |
| 1.4 | Apache-2.0 packages: any NOTICE file is included | look in the package folder for `NOTICE*` | ✅ none of SQLitePCLRaw 2.1.13 or Serilog 4.3.0 / 10.0.0 / 7.0.0 ships one |
| 1.5 | ONNX Runtime telemetry is off | `OnnxPrivacy`; after an ONNX Runtime upgrade, reread its `Privacy.md` for the switches | ✅ 1.31.1 |
| 1.6 | No network connections except model downloads the user starts | run the app (or `FrameForge.Evaluation benchmark`) with models installed while watching `Get-NetTCPConnection -OwningProcess` | ✅ none seen (ADR 0018, with its limits) |
| 1.7 | About shows the notices and the licence folder | open About | ✅ |
| 1.8 | No product names, logos or assets of others in the app | review new assets | ✅ none (FrameForge has no image assets yet) |

## 2. Optional AI models (downloaded by the user, not shipped)
The app shows each model's licence and source and asks before downloading (ADR 0005). The files come from the
sources below, unchanged and checked by SHA-256.

| Model | Licence | Evidence | Notes | Status |
|---|---|---|---|---|
| YuNet 2026may | MIT | `LICENSE` at the pinned OpenCV Zoo commit | | ✅ |
| DINOv2-small (onnx-community export, revision 8b1f705) | Apache-2.0 | facebookresearch/dinov2 README: "code and model weights are released under the Apache License 2.0"; `facebook/dinov2-small` card: apache-2.0 | The ONNX export's page states no licence of its own; we rely on the base model's. The licence link in `AiModelCatalog` points at `main`, not a pinned commit. The non-commercial licences in that repository cover Cell-DINO and XRay-DINO, which FrameForge doesn't use. | ⚠️ acceptable; pin the link when the catalog is next changed |
| YOLOX-s 2022nov | Apache-2.0 | `LICENSE` at the pinned OpenCV Zoo commit | Trained on COCO: annotations CC BY 4.0, images under Flickr terms. Attributed in the notices. | ✅ |
| MobileSAM (samexporter export 5050a79) | Apache-2.0 | `LICENSE` at the pinned MobileSAM commit | | ✅ |

**If models are ever bundled with the app** rather than downloaded:
- ship each model's licence text in `licenses/`
- keep the attributions
- for DINOv2, confirm the export's licence first

## 3. Not shipped (no obligations)
- **Development and test tools:** xunit.v3, dotnet-ef, Microsoft.EntityFrameworkCore.Design.
- **The evaluation tool:** `tools/FrameForge.Evaluation` is never distributed. It uses the same packages, plus LibRaw
  for the RAW spike, which is loaded from a path given on the command line and isn't in the repository.
- **Windows components:** the fonts (Segoe UI Variable or Segoe UI, Segoe Fluent Icons or MDL2 Assets, Cascadia Mono or
  Consolas) and WIC come with Windows. They are used, not redistributed.

## 4. Decisions for later phases
| Phase | What changes | What it needs |
|---|---|---|
| E: installer | If the installer bundles the .NET runtime (self-contained publish) | ship its `LICENSE.TXT` (MIT) and `THIRD-PARTY-NOTICES.TXT`, from the runtime pack, in `licenses/` |
| E: installer | The installer tool itself (WiX, MSIX, Inno Setup …) | check its licence covers redistributing what it produces; add any runtime it installs |
| E: crash reports | Any upload | must be opt-in, explained, and listed here and in About; it changes the "sends nothing" statement |
| A: RAW | LibRaw (`libraw.dll`), dual-licensed LGPL-2.1 or CDDL-1.0 | **Choose one.** LGPL-2.1: keep it a separate, replaceable DLL (dynamic linking, as now), ship its licence and source offer or a link to the exact source, and don't modify it without publishing the changes. CDDL-1.0: ship the licence and keep any changes to its files open. Either way, add its notices and its own third-party notices to `licenses/`. Record the choice in an ADR. |
