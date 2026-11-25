using FrameForge.Application.Editing;

namespace FrameForge.Application.Export;

/// <summary>What an export produces.</summary>
public enum ExportMode
{
    /// <summary>Byte-for-byte copies of the originals.</summary>
    CopyFiles,

    /// <summary>A CSV listing the selected source files.</summary>
    Manifest,

    /// <summary>Render each photo's edit into a new JPEG.</summary>
    EditedJpegs,
}

/// <summary>
/// A named set of export settings. The destination folder is not part of a profile: it is chosen for each export.
/// Settings that don't apply to <see cref="Mode"/> are kept, so switching mode doesn't lose them.
/// </summary>
public sealed record ExportProfile
{
    public const int MaxNameLength = 60;

    public required string Name { get; init; }

    public ExportMode Mode { get; init; } = ExportMode.CopyFiles;

    /// <summary>File-name pattern (see <see cref="ExportNameTemplate"/>); empty keeps the original names.</summary>
    public string NamePattern { get; init; } = string.Empty;

    public int Quality { get; init; } = EditedExportOptions.Default.Quality;

    public int? MaxLongSide { get; init; }

    public ExportMetadata Metadata { get; init; } = EditedExportOptions.Default.Metadata;

    public bool WriteRatingAndLabel { get; init; } = EditedExportOptions.Default.WriteRatingAndLabel;

    /// <summary>The name: what lists and screen readers show for a profile.</summary>
    public override string ToString() => Name;

    /// <summary>True when both hold the same settings, whatever their names.</summary>
    public bool HasSameSettings(ExportProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return this with { Name = string.Empty } == other with { Name = string.Empty };
    }

    /// <summary>Problems that make the profile unusable; empty when it is fine.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("A profile needs a name.");
        }
        else if (Name.Trim().Length > MaxNameLength)
        {
            errors.Add($"Profile names can be at most {MaxNameLength} characters.");
        }

        if (!Enum.IsDefined(Mode))
        {
            errors.Add("Unknown export mode.");
        }

        if (NamePattern is null)
        {
            errors.Add("The file-name pattern is missing.");
        }
        else if (!ExportNameTemplate.TryParse(NamePattern, out _, out var patternErrors))
        {
            errors.Add($"The file-name pattern can't be used: {patternErrors[0]}");
        }

        var options = new EditedExportOptions { Quality = Quality, MaxLongSide = MaxLongSide, Metadata = Metadata };
        errors.AddRange(options.Validate());
        return errors;
    }
}

/// <summary>What is stored: the user's own profiles and the settings of the last export. Versioned JSON.</summary>
public sealed record ExportProfileSet
{
    public const int CurrentVersion = 1;

    public static ExportProfileSet Empty { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>The user's profiles, in the order they were first saved. Never contains a built-in name.</summary>
    public IReadOnlyList<ExportProfile> Profiles { get; init; } = [];

    /// <summary>Settings of the most recent export (its name is not shown), or null before the first one.</summary>
    public ExportProfile? LastUsed { get; init; }

    public bool IsValid() =>
        Version == CurrentVersion
        && Profiles is not null
        && Profiles.All(p => p is not null && p.Validate().Count == 0 && !ExportProfiles.IsBuiltInName(p.Name))
        && Profiles.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Profiles.Count
        && (LastUsed is null || LastUsed.Validate().Count == 0);
}

/// <summary>The built-in profiles and the rules for adding and removing the user's own.</summary>
public static class ExportProfiles
{
    public static ExportProfile FullSizeJpeg { get; } = new()
    {
        Name = "Full-size JPEG",
        Mode = ExportMode.EditedJpegs,
        Quality = EditedExportOptions.Default.Quality,
        Metadata = ExportMetadata.All,
        WriteRatingAndLabel = true,
    };

    /// <summary>For sharing online: smaller files, no GPS or place names, and no personal rating or label.</summary>
    public static ExportProfile Web { get; } = new()
    {
        Name = "Web (2048 px, no location)",
        Mode = ExportMode.EditedJpegs,
        Quality = 85,
        MaxLongSide = 2048,
        Metadata = ExportMetadata.AllButLocation,
        WriteRatingAndLabel = false,
    };

    public static ExportProfile Originals { get; } = new()
    {
        Name = "Originals",
        Mode = ExportMode.CopyFiles,
    };

    public static IReadOnlyList<ExportProfile> BuiltIn { get; } = [FullSizeJpeg, Web, Originals];

    public static bool IsBuiltInName(string? name) =>
        name is not null && BuiltIn.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Adds <paramref name="profile"/>, replacing a user profile of the same name (ignoring case). The name is trimmed.
    /// </summary>
    /// <exception cref="ArgumentException">The profile is not valid or uses a built-in name.</exception>
    public static ExportProfileSet Save(ExportProfileSet set, ExportProfile profile)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(profile);
        var named = profile with { Name = profile.Name?.Trim() ?? string.Empty };
        if (named.Validate() is { Count: > 0 } errors)
        {
            throw new ArgumentException(errors[0], nameof(profile));
        }

        if (IsBuiltInName(named.Name))
        {
            throw new ArgumentException($"“{named.Name}” is a built-in profile; choose another name.", nameof(profile));
        }

        var profiles = set.Profiles.ToList();
        var index = profiles.FindIndex(p => string.Equals(p.Name, named.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = named;
        }
        else
        {
            profiles.Add(named);
        }

        return set with { Profiles = profiles };
    }

    /// <summary>Removes the user profile called <paramref name="name"/> (ignoring case); built-ins can't be removed.</summary>
    public static ExportProfileSet Delete(ExportProfileSet set, string name)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(name);
        return set with { Profiles = set.Profiles.Where(p => !string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList() };
    }
}
