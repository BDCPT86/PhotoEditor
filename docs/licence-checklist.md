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
| E: installer ✅ done | The release is self-contained, so it bundles the .NET runtime | `build/publish.ps1` copies the runtime packs' `LICENSE` and `THIRD-PARTY-NOTICES` (Microsoft.NETCore.App and Microsoft.WindowsDesktop.App) into the release's `licenses` |
| E: installer ✅ done | Inno Setup 6.7.4 builds the installer (not shipped; the installer it produces is) | Its licence allows "any purpose, including commercial applications". The author asks commercial users (for-profit, above US$5,000 a year) to buy a licence; this is not strictly required, and FrameForge's use is personal. An acknowledgement "would be appreciated but is not required". Downloaded from the official GitHub release; Authenticode signature (Pyrsys B.V.) and GitHub release attestation both verified (ADR 0019). |
| E: installer ⚠️ check before sharing | Microsoft Visual C++ 2015–2022 runtime, `vc_redist.x64.exe` 14.44.35211, bundled and run only when missing (added in A0) | Microsoft's own installer, unmodified and validly signed by Microsoft Corporation (`build/publish.ps1` checks). Redistributing it is allowed as "Distributable Code" under the Visual Studio licence terms; confirm you hold one (the free Community edition is understood to qualify) before giving the installer to anyone else. For your own use, nothing is redistributed. |
| E: crash reports ✅ local only | Reports are written on the computer and never sent | Any future upload must meet the conditions in ADR 0019 (opt-in, shown in full before sending, disclosed here and in About) and changes the "sends nothing" statement |
| A: RAW ✅ chosen; ⚠️ one step before sharing | LibRaw 0.22.3 (`libraw.dll`), dual-licensed LGPL-2.1 or CDDL-1.0 | **LGPL-2.1 chosen** (ADR 0020). Done: the unmodified official DLL is a separate file, loaded at run time from the app folder (`third_party/libraw/0.22.3`, SHA-256 recorded); its licence and copyright ship in `licenses/` and the notices; it stays replaceable (a rebuild of the same version is accepted, only another version is refused). **Before giving FrameForge to anyone else:** ship the source archive https://www.libraw.org/data/LibRaw-0.22.3.tar.gz with it, or a written offer to provide it (LGPL 2.1, section 6). |
