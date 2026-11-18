namespace FrameForge.Application.Ai;

/// <summary>Everything a user needs to decide whether to install a model, and what the app needs to verify it.</summary>
public sealed record ModelDescriptor
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Purpose { get; init; }

    public required string Version { get; init; }

    public required string FileName { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the exact file; downloads and imports must match.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Download URL pinned to an immutable commit/revision.</summary>
    public required Uri DownloadUrl { get; init; }

    public required Uri SourcePage { get; init; }

    public required string Licence { get; init; }

    public required Uri LicenceUrl { get; init; }

    public required string Copyright { get; init; }

    public string Notes { get; init; } = string.Empty;

    /// <summary>Recorded with every result so a different model file always invalidates cached output.</summary>
    public string VersionTag => $"{Id}@{Version}#{Sha256[..12]}";
}

/// <summary>
/// The models FrameForge knows how to use. Checksums and sizes were recorded from the pinned files on 2026-11-16.
/// Models are never bundled or downloaded without explicit user consent.
/// </summary>
public static class AiModelCatalog
{
    public static ModelDescriptor FaceDetector { get; } = new()
    {
        Id = "yunet-face-detector",
        Name = "YuNet face detector",
        Purpose = "Finds faces (boxes and 5 landmarks) so FrameForge can check whether faces are sharp. Detection only — no recognition or identity.",
        Version = "2026may",
        FileName = "face_detection_yunet_2026may.onnx",
        SizeBytes = 229_738,
        Sha256 = "ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0",
        DownloadUrl = new Uri("https://github.com/opencv/opencv_zoo/raw/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/face_detection_yunet/face_detection_yunet_2026may.onnx"),
        SourcePage = new Uri("https://github.com/opencv/opencv_zoo/tree/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/face_detection_yunet"),
        Licence = "MIT",
        LicenceUrl = new Uri("https://github.com/opencv/opencv_zoo/blob/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/face_detection_yunet/LICENSE"),
        Copyright = "Copyright (c) 2020 Shiqi Yu",
        Notes = "OpenCV Zoo, dynamic input shape. Wu, Peng, Yu: \"YuNet: A tiny millisecond-level face detector\" (2023).",
    };

    public static ModelDescriptor SceneEmbedder { get; } = new()
    {
        Id = "dinov2-small",
        Name = "DINOv2 small (image embeddings)",
        Purpose = "Turns each photo into a 384-number description so visually similar scenes can be grouped, even when reframed.",
        Version = "hf-8b1f705",
        FileName = "dinov2-small.onnx",
        SizeBytes = 88_532_934,
        Sha256 = "f22797eabf810a75e41de68d378541ebea372122b25c4ce3ef25ff618250c20a",
        DownloadUrl = new Uri("https://huggingface.co/onnx-community/dinov2-small/resolve/8b1f705a3a7f6f062f6bdd21986c1583d3ef105d/onnx/model.onnx"),
        SourcePage = new Uri("https://huggingface.co/onnx-community/dinov2-small/tree/8b1f705a3a7f6f062f6bdd21986c1583d3ef105d"),
        Licence = "Apache-2.0",
        LicenceUrl = new Uri("https://github.com/facebookresearch/dinov2/blob/main/LICENSE"),
        Copyright = "Copyright (c) Meta Platforms, Inc. and affiliates",
        Notes = "ONNX conversion of Meta's facebook/dinov2-small (Apache-2.0 code and weights) published in onnx-community; the conversion repository itself carries no licence tag.",
    };

    /// <summary>Milestone 08 (ADR 0008): finds the people that the subject mask is made of.</summary>
    public static ModelDescriptor PersonDetector { get; } = new()
    {
        Id = "yolox-s-person",
        Name = "YOLOX-s person detector",
        Purpose = "Finds whole people (not faces) so Select subject knows who to mask. Needed with the two MobileSAM files. Detection only — no recognition or identity.",
        Version = "2022nov",
        FileName = "object_detection_yolox_2022nov.onnx",
        SizeBytes = 35_858_002,
        Sha256 = "c5c2d13e59ae883e6af3b45daea64af4833a4951c92d116ec270d9ddbe998063",
        DownloadUrl = new Uri("https://media.githubusercontent.com/media/opencv/opencv_zoo/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/object_detection_yolox/object_detection_yolox_2022nov.onnx"),
        SourcePage = new Uri("https://github.com/opencv/opencv_zoo/tree/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/object_detection_yolox"),
        Licence = "Apache-2.0",
        LicenceUrl = new Uri("https://github.com/opencv/opencv_zoo/blob/26cc381e4d2594bb9f47a26eb8fd96c94a13660d/models/object_detection_yolox/LICENSE"),
        Copyright = "Copyright (c) 2021-2022 Megvii Inc.",
        Notes = "OpenCV Zoo export of YOLOX-s, trained on COCO (annotations CC BY 4.0; images under their Flickr terms). Only the person class is used.",
    };

    /// <summary>Milestone 08 (ADR 0008): MobileSAM's image encoder, run once per photo.</summary>
    public static ModelDescriptor SubjectMaskEncoder { get; } = new()
    {
        Id = "mobile-sam-encoder",
        Name = "MobileSAM encoder (subject masks)",
        Purpose = "Part 1 of 2 of the subject mask model: reads the photo once so people can be outlined. Needed with the MobileSAM decoder and the person detector.",
        Version = "f706ad9",
        FileName = "mobile_sam.encoder.onnx",
        SizeBytes = 28_157_853,
        Sha256 = "8c1494f7dc70b61b15bc7ab8e4291804d47294d881da137b099b04c1776535dd",
        DownloadUrl = new Uri("https://huggingface.co/nrl-ai/samexporter-onnx-models/resolve/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam/mobile_sam.encoder.onnx"),
        SourcePage = new Uri("https://huggingface.co/nrl-ai/samexporter-onnx-models/tree/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam"),
        Licence = "Apache-2.0",
        LicenceUrl = new Uri("https://github.com/ChaoningZhang/MobileSAM/blob/f706ad9c4eb7f219c00d9050e46328518ffb65d2/LICENSE"),
        Copyright = "MobileSAM authors (Chaoning Zhang et al.); the licence names no holder",
        Notes = "samexporter ONNX export of the official MobileSAM checkpoint at upstream commit f706ad9 (the export folder carries an Apache-2.0 LICENSE).",
    };

    /// <summary>Milestone 08 (ADR 0008): MobileSAM's prompt decoder, run once per person or click.</summary>
    public static ModelDescriptor SubjectMaskDecoder { get; } = new()
    {
        Id = "mobile-sam-decoder",
        Name = "MobileSAM decoder (subject masks)",
        Purpose = "Part 2 of 2 of the subject mask model: outlines each person, and follows your clicks to add or remove parts.",
        Version = "f706ad9",
        FileName = "mobile_sam.decoder.onnx",
        SizeBytes = 16_502_895,
        Sha256 = "23c11087a0c1930d863ba37fdf0f2b0080f5a94269191f679ffd16b8415f0a38",
        DownloadUrl = new Uri("https://huggingface.co/nrl-ai/samexporter-onnx-models/resolve/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam/mobile_sam.decoder.onnx"),
        SourcePage = new Uri("https://huggingface.co/nrl-ai/samexporter-onnx-models/tree/5050a79cd4b912dd745fff83047c4ef6fbd97be5/mobile_sam"),
        Licence = "Apache-2.0",
        LicenceUrl = new Uri("https://github.com/ChaoningZhang/MobileSAM/blob/f706ad9c4eb7f219c00d9050e46328518ffb65d2/LICENSE"),
        Copyright = "MobileSAM authors (Chaoning Zhang et al.); the licence names no holder",
        Notes = "samexporter ONNX export of the official MobileSAM checkpoint at upstream commit f706ad9.",
    };

    /// <summary>The files Select subject needs, all three.</summary>
    public static IReadOnlyList<ModelDescriptor> SubjectMaskModels { get; } = [PersonDetector, SubjectMaskEncoder, SubjectMaskDecoder];

    public static IReadOnlyList<ModelDescriptor> All { get; } = [FaceDetector, SceneEmbedder, PersonDetector, SubjectMaskEncoder, SubjectMaskDecoder];
}
