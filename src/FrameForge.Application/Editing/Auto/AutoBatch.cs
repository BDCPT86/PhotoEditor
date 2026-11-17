using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Rendering;

namespace FrameForge.Application.Editing.Auto;

/// <summary>
/// What to apply when running Auto on many photos. Each step names the option to use in advance, because nobody is
/// there to pick one per photo; the "Auto-applied" filter is for reviewing afterwards.
/// </summary>
public sealed record AutoBatchOptions
{
    /// <summary>"Natural", "Brighter" or "Punchy"; null skips Auto light.</summary>
    public string? Light { get; init; }

    /// <summary>Level to the main line when one is found.</summary>
    public bool Level { get; init; }

    /// <summary>"Tight", "Medium" or "Wide"; null skips Auto crop.</summary>
    public string? Crop { get; init; }

    /// <summary>"1:1", "4:5", "3:2", "16:9", or null/"Original" for each photo's own ratio. 3:2 and 16:9 follow each photo's orientation.</summary>
    public string? CropAspect { get; init; }

    public bool Any => Light is not null || Level || Crop is not null;
}

/// <summary>Runs the chosen Auto steps on one photo: light, then level, then crop (crop last, on the levelled frame).</summary>
public static class AutoBatch
{
    /// <summary>Analysis size: enough for statistics and lines, cheap to decode.</summary>
    public const int WorkSize = 1024;

    /// <returns>
    /// The new recipe (null when nothing changed) and a note on any step that could not be applied, e.g. no faces to
    /// frame. A step that cannot apply never blocks the others.
    /// </returns>
    public static async Task<(EditRecipe? After, string? Note)> ApplyAsync(
        Photo photo,
        EditRecipe current,
        AutoBatchOptions options,
        IEditRenderService renderer,
        IFaceLookup faces,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(faces);

        var notes = new List<string>();
        var recipe = current;
        if (options.Light is not null || options.Level)
        {
            var source = await renderer.LoadSourceAsync(photo, WorkSize, cancellationToken).ConfigureAwait(false);
            if (options.Light is { } light)
            {
                recipe = AutoTone.Suggest(source, recipe).Named(light)?.Recipe ?? recipe;
            }

            if (options.Level)
            {
                var level = AutoStraighten.Suggest(source, recipe, cancellationToken);
                if (level.Named("Main line") is { } main)
                {
                    recipe = main.Recipe;
                }
                else if (level.Unavailable is { } why && !why.StartsWith("Already level", StringComparison.Ordinal))
                {
                    notes.Add("not levelled (no long straight line)");
                }
            }
        }

        if (options.Crop is { } variant)
        {
            if (!faces.HasFaceData(photo.Id))
            {
                notes.Add("not cropped (not analysed for faces)");
            }
            else
            {
                var (w, h) = EditRenderService.OrientedSize(photo) ?? (4000, 3000);
                var ratio = RatioFor(options.CropAspect, FrameRatio(w, h, recipe));
                var crop = AutoCrop.Suggest(w, h, faces.FacesOf(photo.Id), recipe, ratio, faces.FaceMinScore);

                // A variant can merge with a neighbour (e.g. Tight grown to the minimum size); use the nearest one left.
                var chosen = crop.Named(variant) ?? (crop.Options.Count == 0 ? null : variant == "Tight" ? crop.Options[0] : crop.Options[^1]);
                if (chosen is not null)
                {
                    recipe = chosen.Recipe;
                }
                else
                {
                    notes.Add("not cropped (no main face)");
                }
            }
        }

        return (recipe == current ? null : recipe, notes.Count == 0 ? null : string.Join("; ", notes));
    }

    /// <summary>The pixel ratio a crop aspect choice means for a photo whose frame ratio is <paramref name="frameRatio"/>.</summary>
    public static double? RatioFor(string? aspect, double frameRatio) => aspect switch
    {
        "1:1" => 1,
        "4:5" => 4.0 / 5,
        "3:2" => frameRatio >= 1 ? 3.0 / 2 : 2.0 / 3,
        "16:9" => frameRatio >= 1 ? 16.0 / 9 : 9.0 / 16,
        _ => null,
    };

    public static double FrameRatio(int width, int height, EditRecipe recipe)
    {
        var g = new EditGeometry(width, height, recipe with { Crop = CropRect.Full });
        return g.OutputWidth / g.OutputHeight;
    }
}
