using System.Globalization;

namespace FrameForge.Core.Editing;

/// <summary>
/// Crop rectangle in normalised coordinates (0–1) of the straightened frame: the largest rectangle with the image's
/// aspect ratio that fits inside the straightened photo. Any rectangle inside 0–1 is therefore free of blank corners.
/// </summary>
public sealed record CropRect(double X, double Y, double Width, double Height)
{
    public static CropRect Full { get; } = new(0, 0, 1, 1);

    /// <summary>Smallest crop side, as a share of the frame, so a crop always keeps some pixels.</summary>
    public const double MinSide = 0.02;

    public bool IsFull => X == 0 && Y == 0 && Width == 1 && Height == 1;
}

/// <summary>
/// A non-destructive edit: the settings applied when a photo is previewed or exported. The original file is never
/// changed. Every slider is 0 when neutral; a neutral recipe renders the photo unchanged.
/// Instances can be built from untrusted JSON, so call <see cref="Validate"/> at every boundary.
/// </summary>
public sealed record EditRecipe
{
    /// <summary>
    /// Bump when the meaning of a stored field changes; older recipes are migrated on load.
    /// <list type="bullet">
    /// <item>1: global light, colour, crop and straighten (Milestone 06).</item>
    /// <item>2: adds <see cref="LocalAdjustments"/> (Milestone 08). A recipe without them is still stored as version 1,
    /// in exactly the version 1 form, so it stays readable by earlier versions of the app.</item>
    /// </list>
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public const double MaxExposure = 5;
    public const double MaxSlider = 100;
    public const double MaxStraighten = 45;

    /// <summary>Most local adjustments one photo can have; each needs a mask computed, so keep it small.</summary>
    public const int MaxLocalAdjustments = 8;

    public static EditRecipe Neutral { get; } = new();

    /// <summary>
    /// The version the recipe was written as. In memory it is always <see cref="CurrentSchemaVersion"/> (recipes are
    /// upgraded when read); storage writes <see cref="RequiredSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Stops (EV), −5 to +5. +1 doubles the light.</summary>
    public double Exposure { get; init; }

    /// <summary>−100 (cooler/bluer) to +100 (warmer/yellower), relative to the photo as shot.</summary>
    public double Temperature { get; init; }

    /// <summary>−100 (greener) to +100 (more magenta).</summary>
    public double Tint { get; init; }

    public double Contrast { get; init; }

    /// <summary>Negative darkens bright tones (as far as 8-bit data allows; clipped detail cannot return).</summary>
    public double Highlights { get; init; }

    /// <summary>Positive lifts dark tones.</summary>
    public double Shadows { get; init; }

    public double Saturation { get; init; }

    /// <summary>Like saturation, but acts mostly on muted colours.</summary>
    public double Vibrance { get; init; }

    /// <summary>Clockwise quarter turns, 0–3, applied before straightening.</summary>
    public int QuarterTurns { get; init; }

    /// <summary>Degrees, −45 to +45; positive rotates the picture clockwise.</summary>
    public double Straighten { get; init; }

    public CropRect Crop { get; init; } = CropRect.Full;

    /// <summary>Settings applied inside masks (subject, sky, background), in order; empty for most photos.</summary>
    public ValueList<LocalAdjustment> LocalAdjustments { get; init; } = ValueList.Empty<LocalAdjustment>();

    public bool HasGeometry => QuarterTurns != 0 || Straighten != 0 || !Crop.IsFull;

    public bool HasTone => Exposure != 0 || Temperature != 0 || Tint != 0 || Contrast != 0 || Highlights != 0 || Shadows != 0;

    public bool HasColour => Saturation != 0 || Vibrance != 0;

    /// <summary>True when the recipe has masks, even ones whose sliders are still at 0 (they are kept, ready to adjust).</summary>
    public bool HasLocalAdjustments => LocalAdjustments is { Count: > 0 };

    /// <summary>A neutral recipe renders the photo unchanged and is not stored.</summary>
    public bool IsNeutral => !HasGeometry && !HasTone && !HasColour && !HasLocalAdjustments;

    /// <summary>The oldest version that can hold this recipe: 1 unless it has local adjustments.</summary>
    public int RequiredSchemaVersion => HasLocalAdjustments ? 2 : 1;

    /// <summary>All problems with this recipe; empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion < 1 || SchemaVersion > CurrentSchemaVersion)
        {
            errors.Add(Invariant($"Unsupported recipe version {SchemaVersion} (this app reads up to {CurrentSchemaVersion})."));
        }
        else if (SchemaVersion < RequiredSchemaVersion)
        {
            errors.Add(Invariant($"Local adjustments need recipe version {RequiredSchemaVersion}, but the recipe says version {SchemaVersion}."));
        }

        if (LocalAdjustments is null)
        {
            errors.Add("Local adjustments are missing.");
        }
        else
        {
            if (LocalAdjustments.Count > MaxLocalAdjustments)
            {
                errors.Add(Invariant($"At most {MaxLocalAdjustments} local adjustments, got {LocalAdjustments.Count}."));
            }

            for (var i = 0; i < LocalAdjustments.Count; i++)
            {
                if (LocalAdjustments[i] is not { } adjustment)
                {
                    errors.Add(Invariant($"Local adjustment {i + 1} is missing."));
                    continue;
                }

                adjustment.Validate(errors, i);
            }

            if (LocalAdjustments.Where(a => a is not null).GroupBy(a => a.Id).Any(g => g.Count() > 1))
            {
                errors.Add("Local adjustment ids must be unique.");
            }
        }

        Range(errors, nameof(Exposure), Exposure, -MaxExposure, MaxExposure);
        foreach (var (name, value) in new[]
        {
            (nameof(Temperature), Temperature), (nameof(Tint), Tint), (nameof(Contrast), Contrast), (nameof(Highlights), Highlights),
            (nameof(Shadows), Shadows), (nameof(Saturation), Saturation), (nameof(Vibrance), Vibrance),
        })
        {
            Range(errors, name, value, -MaxSlider, MaxSlider);
        }

        Range(errors, nameof(Straighten), Straighten, -MaxStraighten, MaxStraighten);
        if (QuarterTurns is < 0 or > 3)
        {
            errors.Add(Invariant($"{nameof(QuarterTurns)} must be 0–3, got {QuarterTurns}."));
        }

        if (Crop is null)
        {
            errors.Add("Crop is missing.");
        }
        else
        {
            var c = Crop;
            if (!new[] { c.X, c.Y, c.Width, c.Height }.All(double.IsFinite)
                || c.X < 0 || c.Y < 0 || c.Width < CropRect.MinSide || c.Height < CropRect.MinSide
                || c.X + c.Width > 1 + 1e-9 || c.Y + c.Height > 1 + 1e-9)
            {
                errors.Add(Invariant($"Crop must lie within 0–1 with sides of at least {CropRect.MinSide}, got ({c.X}, {c.Y}, {c.Width}, {c.Height})."));
            }
        }

        return errors;
    }

    /// <exception cref="ArgumentException">The recipe is invalid.</exception>
    public EditRecipe EnsureValid()
    {
        var errors = Validate();
        return errors.Count == 0 ? this : throw new ArgumentException("Invalid edit recipe: " + string.Join(" ", errors));
    }

    internal static void Range(List<string> errors, string name, double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            errors.Add(Invariant($"{name} must be between {min} and {max}, got {value}."));
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
