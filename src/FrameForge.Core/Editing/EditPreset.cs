namespace FrameForge.Core.Editing;

/// <summary>Groups of settings a preset can carry. A preset only changes the groups it includes.</summary>
[Flags]
public enum PresetGroups
{
    None = 0,
    Exposure = 1,
    WhiteBalance = 2,
    Tone = 4,          // contrast, highlights, shadows
    Colour = 8,        // saturation, vibrance
    Geometry = 16,     // quarter turns, straighten, crop

    /// <summary>
    /// The Milestone 06 groups. Presets are stored by group name, so this keeps its meaning: an "All" preset saved
    /// before masks existed must not start replacing a photo's masks.
    /// </summary>
    All = Exposure | WhiteBalance | Tone | Colour | Geometry,

    /// <summary>Local adjustments (Milestone 08): the preset's masks and their settings replace the photo's.</summary>
    Masks = 32,
}

/// <summary>A named set of settings that can be applied to many photos.</summary>
public sealed record EditPreset(Guid Id, string Name, EditRecipe Recipe, PresetGroups Groups)
{
    public const int MaxNameLength = 80;

    /// <summary>Settings from <see cref="Recipe"/> for the included groups; everything else from <paramref name="target"/>.</summary>
    public EditRecipe ApplyTo(EditRecipe target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var r = target;
        if (Groups.HasFlag(PresetGroups.Exposure))
        {
            r = r with { Exposure = Recipe.Exposure };
        }

        if (Groups.HasFlag(PresetGroups.WhiteBalance))
        {
            r = r with { Temperature = Recipe.Temperature, Tint = Recipe.Tint };
        }

        if (Groups.HasFlag(PresetGroups.Tone))
        {
            r = r with { Contrast = Recipe.Contrast, Highlights = Recipe.Highlights, Shadows = Recipe.Shadows };
        }

        if (Groups.HasFlag(PresetGroups.Colour))
        {
            r = r with { Saturation = Recipe.Saturation, Vibrance = Recipe.Vibrance };
        }

        if (Groups.HasFlag(PresetGroups.Geometry))
        {
            r = r with { QuarterTurns = Recipe.QuarterTurns, Straighten = Recipe.Straighten, Crop = Recipe.Crop };
        }

        if (Groups.HasFlag(PresetGroups.Masks))
        {
            // Clicks belong to the photo they were made on, so they are not carried over: each photo's masks are
            // found automatically and can then be refined there.
            r = r with
            {
                LocalAdjustments = new(Recipe.LocalAdjustments.Select(a => a with { Mask = a.Mask with { Points = ValueList.Empty<MaskPoint>() } })),
            };
        }

        return r;
    }

    /// <summary>Every group a preset can include.</summary>
    public const PresetGroups KnownGroups = PresetGroups.All | PresetGroups.Masks;

    /// <summary>All problems with this preset; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("A preset needs a name.");
        }
        else if (Name.Trim().Length > MaxNameLength)
        {
            errors.Add($"Preset names can be at most {MaxNameLength} characters.");
        }

        if (Groups == PresetGroups.None || (Groups & ~KnownGroups) != 0)
        {
            errors.Add("A preset must include at least one known group of settings.");
        }

        if (Recipe is null)
        {
            errors.Add("A preset needs settings.");
        }
        else
        {
            errors.AddRange(Recipe.Validate());
        }

        return errors;
    }
}
