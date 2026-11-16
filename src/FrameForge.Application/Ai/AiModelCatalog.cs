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

    public static IReadOnlyList<ModelDescriptor> All { get; } = [FaceDetector, SceneEmbedder];
}
