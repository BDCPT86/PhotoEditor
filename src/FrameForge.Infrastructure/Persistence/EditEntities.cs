namespace FrameForge.Infrastructure.Persistence;

public sealed class PhotoEditEntity
{
    public Guid PhotoId { get; set; }

    public int SchemaVersion { get; set; }

    public string RecipeJson { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; }
}

public sealed class EditPresetEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Comma-separated <c>PresetGroups</c> names.</summary>
    public string Groups { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public string RecipeJson { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
