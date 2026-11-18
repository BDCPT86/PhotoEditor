using System.Globalization;

namespace FrameForge.Core.Editing;

/// <summary>What a mask selects. Background is <see cref="Subject"/> with <see cref="MaskReference.Invert"/> set.</summary>
public enum MaskKind
{
    /// <summary>The main people, found by a person detector and outlined by a segmentation model (ADR 0008).</summary>
    Subject,

    /// <summary>The sky, found without a model.</summary>
    Sky,
}

/// <summary>
/// A click that adds to (<see cref="Include"/>) or removes from a mask, in normalised coordinates (0–1) of the upright
/// photo before crop and straighten, so it stays on the same spot when the crop changes.
/// </summary>
public sealed record MaskPoint(double X, double Y, bool Include);

/// <summary>
/// How to make a mask: never the mask itself. Masks are derived data, computed from the original and cached on disk
/// by photo identity, <see cref="Source"/> and these parameters, so a stored recipe stays small.
/// </summary>
public sealed record MaskReference
{
    public const int MaxSourceLength = 200;
    public const int MaxPoints = 50;

    public required MaskKind Kind { get; init; }

    /// <summary>
    /// What made the mask, with its version (a model's version tag, or the name and version of a non-model method).
    /// Recorded so a different model or method is noticed and the mask recomputed.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Selects everything except the mask (Subject inverted is Background).</summary>
    public bool Invert { get; init; }

    /// <summary>Clicks that refine the mask; empty for a fully automatic mask.</summary>
    public ValueList<MaskPoint> Points { get; init; } = ValueList.Empty<MaskPoint>();

    /// <summary>The name shown to the user.</summary>
    public string DisplayName => (Kind, Invert) switch
    {
        (MaskKind.Subject, false) => "Subject",
        (MaskKind.Subject, true) => "Background",
        (MaskKind.Sky, false) => "Sky",
        _ => "Not sky",
    };

    internal void Validate(List<string> errors, string prefix)
    {
        if (!Enum.IsDefined(Kind))
        {
            errors.Add(Invariant($"{prefix}: unknown mask kind {(int)Kind}."));
        }

        if (string.IsNullOrWhiteSpace(Source) || Source.Length > MaxSourceLength)
        {
            errors.Add(Invariant($"{prefix}: the mask needs a source of 1–{MaxSourceLength} characters."));
        }

        if (Points is null)
        {
            errors.Add(Invariant($"{prefix}: mask points are missing."));
            return;
        }

        if (Points.Count > MaxPoints)
        {
            errors.Add(Invariant($"{prefix}: at most {MaxPoints} mask points, got {Points.Count}."));
        }

        foreach (var p in Points)
        {
            if (p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1)
            {
                errors.Add(Invariant($"{prefix}: mask points must lie within 0–1."));
                break;
            }
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Light and colour settings applied only inside a mask, on top of the global ones. The sliders have the same meaning
/// and ranges as <see cref="EditRecipe"/>'s; all 0 changes nothing.
/// </summary>
public sealed record LocalAdjustment
{
    /// <summary>Stable within a recipe, so the UI and undo can follow an adjustment while its settings change.</summary>
    public required Guid Id { get; init; }

    public required MaskReference Mask { get; init; }

    public double Exposure { get; init; }

    public double Temperature { get; init; }

    public double Tint { get; init; }

    public double Contrast { get; init; }

    public double Highlights { get; init; }

    public double Shadows { get; init; }

    public double Saturation { get; init; }

    public double Vibrance { get; init; }

    /// <summary>True when the settings change nothing (the mask may still be kept, ready to adjust).</summary>
    public bool HasNoEffect =>
        Exposure == 0 && Temperature == 0 && Tint == 0 && Contrast == 0 && Highlights == 0 && Shadows == 0 && Saturation == 0 && Vibrance == 0;

    internal void Validate(List<string> errors, int index)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"Local adjustment {index + 1}");
        if (Id == Guid.Empty)
        {
            errors.Add(prefix + ": the id is missing.");
        }

        if (Mask is null)
        {
            errors.Add(prefix + ": the mask is missing.");
        }
        else
        {
            Mask.Validate(errors, prefix);
        }

        EditRecipe.Range(errors, prefix + ": " + nameof(Exposure), Exposure, -EditRecipe.MaxExposure, EditRecipe.MaxExposure);
        foreach (var (name, value) in new[]
        {
            (nameof(Temperature), Temperature), (nameof(Tint), Tint), (nameof(Contrast), Contrast), (nameof(Highlights), Highlights),
            (nameof(Shadows), Shadows), (nameof(Saturation), Saturation), (nameof(Vibrance), Vibrance),
        })
        {
            EditRecipe.Range(errors, prefix + ": " + name, value, -EditRecipe.MaxSlider, EditRecipe.MaxSlider);
        }
    }
}
