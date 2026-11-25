# Third-party notices

FrameForge AI uses the following third-party software. None of it requires a fee, and each part is used under its own
licence. The full licence and notice texts are in the `licenses` folder next to the app. Its index is
[licenses/README.md](licenses/README.md).

## Shipped with the app

All packages the app uses, including those pulled in by other packages (audited for Milestone 07, phase F; see
`docs/licence-checklist.md`).

| Component | Version | Licence | Copyright / source |
|---|---|---|---|
| ONNX Runtime (Microsoft.ML.OnnxRuntime and .Managed, incl. native `onnxruntime.dll`) | 1.31.1 | MIT | © Microsoft Corporation — https://github.com/microsoft/onnxruntime |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | © .NET Foundation and Contributors — https://github.com/CommunityToolkit/dotnet |
| Microsoft.EntityFrameworkCore, .Relational, .Sqlite, .Sqlite.Core, .Abstractions, .Analyzers; Microsoft.Data.Sqlite.Core | 10.0.15 | MIT | © .NET Foundation and Contributors — https://github.com/dotnet/efcore |
| Microsoft.Extensions.* (Hosting, DependencyInjection, Logging, Configuration, Options, Caching, Diagnostics, FileProviders, FileSystemGlobbing, DependencyModel, Primitives) | 10.0.15 | MIT | © .NET Foundation and Contributors — https://github.com/dotnet/runtime |
| System.Numerics.Tensors | 9.0.0 | MIT | © .NET Foundation and Contributors — https://github.com/dotnet/runtime |
| SQLitePCLRaw (bundle_e_sqlite3, core, lib.e_sqlite3, provider.e_sqlite3) | 2.1.13 | Apache-2.0 | © SourceGear, LLC (Eric Sink) — https://github.com/ericsink/SQLitePCL.raw |
| SQLite (`e_sqlite3`) | via SQLitePCLRaw 2.1.13 | Public domain | https://sqlite.org/copyright.html |
| Serilog | 4.3.0 | Apache-2.0 | © Serilog Contributors — https://github.com/serilog/serilog |
| Serilog.Extensions.Hosting, Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | © Serilog Contributors — https://github.com/serilog |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | © Serilog Contributors — https://github.com/serilog/serilog-sinks-file |
| VirtualizingWrapPanel | 2.5.4 | MIT | © sbaeumlisberger — https://github.com/sbaeumlisberger/VirtualizingWrapPanel |
| LibRaw (`libraw.dll`, unmodified official Windows build, separate and replaceable) | 0.22.3 | **LGPL-2.1** (chosen from LGPL-2.1 or CDDL-1.0) | © 2008–2025 LibRaw LLC — https://www.libraw.org/ (source of this version: https://www.libraw.org/data/LibRaw-0.22.3.tar.gz). Reads RAW files (M07 phase A). |

**Privacy:** official ONNX Runtime builds collect usage telemetry and send it to Microsoft by default. FrameForge
turns this off before the runtime starts (ADR 0018). The app makes no network connections except model downloads you
start yourself.

**System components (not shipped):** the app uses the Windows fonts Segoe UI Variable or Segoe UI, Segoe Fluent Icons or Segoe MDL2 Assets,
and Cascadia Mono or Consolas, plus the .NET runtime installed on the computer. None of these is bundled.

## Optional AI models (not shipped; downloaded by the user with consent, from the sources below)

| Model | Version | Licence | Copyright / source |
|---|---|---|---|
| YuNet face detector (`face_detection_yunet_2026may.onnx`) | OpenCV Zoo commit 26cc381e | MIT | Copyright (c) 2020 Shiqi Yu — https://github.com/opencv/opencv_zoo/tree/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/face_detection_yunet |
| DINOv2-small (ONNX export, `onnx-community/dinov2-small`) | revision 8b1f705 | Apache-2.0 | Meta Platforms, Inc. and affiliates — https://github.com/facebookresearch/dinov2 (base model https://huggingface.co/facebook/dinov2-small; export https://huggingface.co/onnx-community/dinov2-small) |
| YOLOX-s person detector (`object_detection_yolox_2022nov.onnx`) | OpenCV Zoo commit 26cc381e | Apache-2.0 | Copyright (c) 2021-2022 Megvii Inc. — https://github.com/opencv/opencv_zoo/tree/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/object_detection_yolox |
| MobileSAM encoder and decoder (`mobile_sam.encoder.onnx`, `mobile_sam.decoder.onnx`, samexporter ONNX export) | upstream commit f706ad9, export revision 5050a79 | Apache-2.0 | MobileSAM authors (Chaoning Zhang et al.) — https://github.com/ChaoningZhang/MobileSAM (export: https://huggingface.co/nrl-ai/samexporter-onnx-models/tree/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam) |

YuNet reference: S. Wu, H. Peng, S. Yu, "YuNet: A Tiny Millisecond-level Face Detector", 2023.
DINOv2 reference: M. Oquab et al., "DINOv2: Learning Robust Visual Features without Supervision", 2023.
YOLOX reference: Z. Ge et al., "YOLOX: Exceeding YOLO Series in 2021", 2021 (trained on COCO: annotations CC BY 4.0, images under their Flickr terms).
MobileSAM reference: C. Zhang et al., "Faster Segment Anything: Towards Lightweight SAM for Mobile Applications", 2023.

## Development only (not shipped)

| Component | Licence |
|---|---|
| xunit.v3 | Apache-2.0 |
| Microsoft.EntityFrameworkCore.Design, dotnet-ef | MIT |
| Inno Setup 6 (builds the installer; installers built with it may be distributed) | Inno Setup License — https://jrsoftware.org/files/is/license.txt |
| LibRaw 0.22.3 in the RAW spike (`tools/FrameForge.Evaluation`, loaded from a path given on the command line) | as above |
