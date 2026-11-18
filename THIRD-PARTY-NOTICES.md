# Third-party notices

FrameForge AI uses the following third-party software. None of it requires a fee. Each component is used under its
own licence. Licence texts are available at the linked sources.

## Shipped with the app

| Component | Version | Licence | Copyright / source |
|---|---|---|---|
| ONNX Runtime (Microsoft.ML.OnnxRuntime, incl. native `onnxruntime.dll`) | 1.31.1 | MIT | © Microsoft Corporation — https://github.com/microsoft/onnxruntime |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | © .NET Foundation and Contributors — https://github.com/CommunityToolkit/dotnet |
| Microsoft.EntityFrameworkCore.Sqlite, Microsoft.Extensions.* | 10.0.15 | MIT | © .NET Foundation and Contributors — https://github.com/dotnet/efcore, https://github.com/dotnet/runtime |
| SQLitePCLRaw (transitive, via EF Core Sqlite) | — | Apache-2.0 | © Eric Sink — https://github.com/ericsink/SQLitePCL.raw |
| SQLite (`e_sqlite3`) | — | Public domain | https://sqlite.org/copyright.html |
| Serilog.Extensions.Hosting, Serilog.Sinks.File (and Serilog) | 10.0.0 / 7.0.0 | Apache-2.0 | © Serilog Contributors — https://github.com/serilog |
| VirtualizingWrapPanel | 2.5.4 | MIT | © S. Bäumlisberger — https://github.com/sbaeumlisberger/VirtualizingWrapPanel |

## Optional AI models (not shipped; installed by the user with consent)

| Model | Version | Licence | Copyright / source |
|---|---|---|---|
| YuNet face detector (`face_detection_yunet_2026may.onnx`) | OpenCV Zoo commit 26cc381e | MIT | Copyright (c) 2020 Shiqi Yu — https://github.com/opencv/opencv_zoo/tree/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/face_detection_yunet |
| DINOv2-small (ONNX export, `onnx-community/dinov2-small`) | revision 8b1f705 | Apache-2.0 | Copyright (c) Meta Platforms, Inc. and affiliates — https://github.com/facebookresearch/dinov2 (export: https://huggingface.co/onnx-community/dinov2-small) |
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
