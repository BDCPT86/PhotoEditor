using FrameForge.Application.Analysis;

namespace FrameForge.Infrastructure.Persistence;

/// <summary>Stored form of <see cref="StoredAnalysis"/>: one row per photo and analysis kind.</summary>
public sealed class AnalysisResultEntity
{
    public Guid PhotoId { get; set; }

    public AnalysisKind Kind { get; set; }

    public required string AlgorithmVersion { get; set; }

    public required string SettingsHash { get; set; }

    public long SourceSizeBytes { get; set; }

    public DateTime SourceModifiedUtc { get; set; }

    public required string ValueJson { get; set; }

    public DateTime CreatedUtc { get; set; }
}
