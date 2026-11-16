using FrameForge.Core.Decisions;

namespace FrameForge.Infrastructure.Persistence;

/// <summary>Stored form of <see cref="PhotoDecision"/>. One row per photo that has a non-empty decision.</summary>
public sealed class PhotoDecisionEntity
{
    public Guid PhotoId { get; set; }

    public CullState State { get; set; }

    public int? Rating { get; set; }

    public ColourLabel Label { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
