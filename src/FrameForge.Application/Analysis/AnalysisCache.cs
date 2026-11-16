using System.Text.Json;
using FrameForge.Analysis;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Analysis;

/// <summary>
/// Cache rules: a stored result is reused only if its algorithm version, settings hash, and source file identity
/// (size and modified time) all match the current ones. Anything else is recomputed.
/// </summary>
public static class AnalysisCache
{
    public const string ContentHashVersion = "sha256-v1";

    public static readonly IReadOnlyList<AnalysisKind> AllKinds =
        [AnalysisKind.ContentHash, AnalysisKind.Sharpness, AnalysisKind.Exposure, AnalysisKind.PerceptualHash];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string VersionOf(AnalysisKind kind) => kind switch
    {
        AnalysisKind.ContentHash => ContentHashVersion,
        AnalysisKind.Sharpness => SharpnessAnalyzer.Version,
        AnalysisKind.Exposure => ExposureAnalyzer.Version,
        AnalysisKind.PerceptualHash => PerceptualHasher.Version,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string SettingsHashOf(AnalysisKind kind, AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return kind switch
        {
            AnalysisKind.ContentHash => AnalysisSettings.ContentHashSettingsHash,
            AnalysisKind.Sharpness => settings.SharpnessSettingsHash,
            AnalysisKind.Exposure => settings.ExposureSettingsHash,
            AnalysisKind.PerceptualHash => settings.PerceptualHashSettingsHash,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>The deterministic kinds this cache handles; AI kinds are validated by the AI service.</summary>
    public static bool Handles(AnalysisKind kind) => AllKinds.Contains(kind);

    public static bool IsValid(StoredAnalysis stored, Photo photo, AnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(photo);

        return Handles(stored.Kind)
            && stored.PhotoId == photo.Id
            && stored.AlgorithmVersion == VersionOf(stored.Kind)
            && stored.SettingsHash == SettingsHashOf(stored.Kind, settings)
            && stored.SourceSizeBytes == photo.FileSizeBytes
            && stored.SourceModifiedUtc == photo.LastModifiedUtc;
    }

    public static StoredAnalysis Create(Photo photo, AnalysisKind kind, AnalysisSettings settings, object value, DateTime nowUtc) =>
        new(photo.Id, kind, VersionOf(kind), SettingsHashOf(kind, settings), photo.FileSizeBytes, photo.LastModifiedUtc,
            JsonSerializer.Serialize(value, value.GetType(), Json), nowUtc);

    /// <summary>Applies one stored value to a typed result. Unreadable JSON is treated as stale (returns the input).</summary>
    public static PhotoAnalysis Apply(PhotoAnalysis analysis, StoredAnalysis stored)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(stored);
        try
        {
            return stored.Kind switch
            {
                AnalysisKind.ContentHash => analysis with { ContentHash = JsonSerializer.Deserialize<string>(stored.ValueJson, Json) },
                AnalysisKind.Sharpness => analysis with { Sharpness = JsonSerializer.Deserialize<SharpnessResult>(stored.ValueJson, Json) },
                AnalysisKind.Exposure => analysis with { Exposure = JsonSerializer.Deserialize<ExposureResult>(stored.ValueJson, Json) },
                AnalysisKind.PerceptualHash => analysis with { PerceptualHash = JsonSerializer.Deserialize<ulong>(stored.ValueJson, Json) },
                _ => analysis,
            };
        }
        catch (JsonException)
        {
            return analysis;
        }
    }
}
