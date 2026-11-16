namespace FrameForge.Infrastructure.Persistence;

public sealed class AppSettingEntity
{
    public required string Key { get; set; }

    public required string ValueJson { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
