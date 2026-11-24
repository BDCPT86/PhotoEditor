using FrameForge.Core.Editing;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Editing.Masks;

/// <summary>
/// Applying a preset that includes masks to many photos (Milestone 08 phase B, B6). Each photo gets the preset, then
/// every mask the preset brings is worked out for that photo (and cached). A mask that can't be made, such as a
/// subject on a photo with no people, is left off that photo and noted; the rest of the preset still applies.
/// </summary>
public static class MaskBatch
{
    /// <summary>
    /// Seconds per photo for a subject mask on a laptop CPU, for the estimate: about 0.5 s when the machine is quiet and
    /// about 3 s when it is busy (B7 benchmark, 4 threads, ADR 0014).
    /// </summary>
    public static readonly (double Low, double High) SubjectSeconds = (0.6, 3);

    /// <summary>Seconds per photo for a sky mask (rules only, plus decoding the photo at 1024 px; B7 benchmark).</summary>
    public static readonly (double Low, double High) SkySeconds = (0.2, 0.6);

    /// <summary>True when the preset brings masks, so applying it means working them out photo by photo.</summary>
    public static bool HasMasks(EditPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return preset.Groups.HasFlag(PresetGroups.Masks) && preset.Recipe.LocalAdjustments.Any(a => !a.HasNoEffect);
    }

    /// <summary>The mask kinds the preset needs (adjustments with settings only).</summary>
    public static IReadOnlySet<MaskKind> KindsNeeded(EditPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return HasMasks(preset)
            ? preset.Recipe.LocalAdjustments.Where(a => !a.HasNoEffect).Select(a => a.Mask.Kind).ToHashSet()
            : new HashSet<MaskKind>();
    }

    /// <summary>A rough range for how long the masks will take on <paramref name="photos"/> photos, or null when none are needed.</summary>
    public static (TimeSpan Low, TimeSpan High)? Estimate(EditPreset preset, int photos)
    {
        var kinds = KindsNeeded(preset);
        if (kinds.Count == 0 || photos <= 0)
        {
            return null;
        }

        double low = 0, high = 0;
        if (kinds.Contains(MaskKind.Subject))
        {
            (low, high) = (low + SubjectSeconds.Low, high + SubjectSeconds.High);
        }

        if (kinds.Contains(MaskKind.Sky))
        {
            (low, high) = (low + SkySeconds.Low, high + SkySeconds.High);
        }

        return (TimeSpan.FromSeconds(Math.Round(low * photos)), TimeSpan.FromSeconds(Math.Round(high * photos)));
    }

    /// <returns>The new recipe (null when nothing changed) and a note naming any mask left off, with the reason.</returns>
    public static async Task<(EditRecipe? After, string? Note)> ApplyAsync(
        Photo photo,
        EditRecipe current,
        EditPreset preset,
        IMaskService masks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(masks);

        var after = preset.ApplyTo(current);
        var notes = new List<string>();
        if (preset.Groups.HasFlag(PresetGroups.Masks))
        {
            var kept = new List<LocalAdjustment>();
            foreach (var adjustment in after.LocalAdjustments)
            {
                if (adjustment.HasNoEffect)
                {
                    kept.Add(adjustment); // nothing to draw, so no mask is needed now; it is found when it gets settings
                    continue;
                }

                var outcome = await masks.GetAsync(photo, adjustment.Mask, cancellationToken).ConfigureAwait(false);
                if (outcome.HasMask)
                {
                    // Keep the source that made it, so the cached mask is found again by the same key.
                    kept.Add(adjustment with { Mask = adjustment.Mask with { Source = outcome.Source } });
                }
                else
                {
                    notes.Add($"{adjustment.Mask.DisplayName} left out: {outcome.Summary}");
                }
            }

            after = after with { LocalAdjustments = kept.Count == 0 ? ValueList.Empty<LocalAdjustment>() : new(kept) };
        }

        return (after == current ? null : after, notes.Count == 0 ? null : string.Join(" ", notes));
    }
}
