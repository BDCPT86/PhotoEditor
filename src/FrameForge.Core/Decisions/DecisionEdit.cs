using System.Globalization;

namespace FrameForge.Core.Decisions;

/// <summary>A single kind of change applied to one or more photos' decisions (e.g. "mark Keep", "rate 3").</summary>
public sealed class DecisionEdit
{
    private readonly Func<PhotoDecision, PhotoDecision> _apply;

    private DecisionEdit(string description, Func<PhotoDecision, PhotoDecision> apply)
    {
        Description = description;
        _apply = apply;
    }

    /// <summary>Short past-tense phrase for status messages, e.g. "Marked Keep".</summary>
    public string Description { get; }

    public PhotoDecision ApplyTo(PhotoDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return _apply(decision);
    }

    public static DecisionEdit SetState(CullState state) => new(
        state == CullState.Unset ? "Cleared decision" : $"Marked {state}",
        d => d.WithState(state));

    /// <param name="rating">1–5, or null to clear.</param>
    public static DecisionEdit SetRating(int? rating)
    {
        // Validate eagerly so an invalid key mapping fails at creation, not on first use.
        _ = PhotoDecision.Unset.WithRating(rating);
        return new(
            rating is null ? "Cleared rating" : string.Create(CultureInfo.InvariantCulture, $"Rated {rating} star{(rating == 1 ? string.Empty : "s")}"),
            d => d.WithRating(rating));
    }

    public static DecisionEdit SetLabel(ColourLabel label)
    {
        _ = PhotoDecision.Unset.WithLabel(label);
        return new(label == ColourLabel.None ? "Cleared label" : $"Labelled {label}", d => d.WithLabel(label));
    }
}
