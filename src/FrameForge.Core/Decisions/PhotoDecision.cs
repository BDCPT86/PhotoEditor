namespace FrameForge.Core.Decisions;

/// <summary>The culling verdict for a photo. Distinct from UI selection and keyboard focus.</summary>
public enum CullState
{
    Unset = 0,
    Keep = 1,
    Review = 2,
    Reject = 3,
}

public enum ColourLabel
{
    None = 0,
    Red = 1,
    Yellow = 2,
    Green = 3,
    Blue = 4,
}

/// <summary>
/// A photo's culling decision: state, optional 1–5 star rating, and colour label.
/// Immutable and always valid; use the <c>With…</c> methods to derive changes.
/// </summary>
public sealed record PhotoDecision
{
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public PhotoDecision(CullState state = CullState.Unset, int? rating = null, ColourLabel label = ColourLabel.None)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown cull state.");
        }

        if (rating is not null && (rating < MinRating || rating > MaxRating))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, $"Rating must be between {MinRating} and {MaxRating}, or null.");
        }

        if (!Enum.IsDefined(label))
        {
            throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown colour label.");
        }

        State = state;
        Rating = rating;
        Label = label;
    }

    /// <summary>No decision: the state of every photo that has never been marked.</summary>
    public static PhotoDecision Unset { get; } = new();

    public CullState State { get; }

    /// <summary>1–5 stars, or null for unrated.</summary>
    public int? Rating { get; }

    public ColourLabel Label { get; }

    /// <summary>True when nothing has been set; such decisions need not be stored.</summary>
    public bool IsEmpty => State == CullState.Unset && Rating is null && Label == ColourLabel.None;

    public PhotoDecision WithState(CullState state) => new(state, Rating, Label);

    public PhotoDecision WithRating(int? rating) => new(State, rating, Label);

    public PhotoDecision WithLabel(ColourLabel label) => new(State, Rating, label);
}
