using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.Services;
using FrameForge.Application.Ai;
using FrameForge.Application.Analysis;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Imaging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// One grid tile. The tile never holds its thumbnail: it asks the bounded memory cache each
/// time the binding reads <see cref="Thumbnail"/>, and requests a load on a miss. That keeps
/// memory bounded regardless of library size.
/// </summary>
public sealed partial class PhotoTileViewModel : ObservableObject, IThumbnailRequester
{
    private readonly IThumbnailSource _thumbnails;
    private bool _requested;
    private string? _details;

    public PhotoTileViewModel(Photo photo, IThumbnailSource thumbnails, PhotoDecision? decision = null)
    {
        Photo = photo;
        _thumbnails = thumbnails;
        _decision = decision ?? PhotoDecision.Unset;
    }

    /// <summary>The photo's culling decision. Set by <see cref="Services.DecisionController"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateGlyph), nameof(StateText), nameof(IsRejected), nameof(RatingText), nameof(Label), nameof(LabelLetter), nameof(AccessibleStatus))]
    private PhotoDecision _decision;

    /// <summary>Heuristic analysis findings, set by <see cref="Services.AnalysisController"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBlurBadge), nameof(ShowClipBadge), nameof(GroupBadgeText), nameof(ShowExactBadge), nameof(ShowSharpestBadge), nameof(HasAnalysisBadges), nameof(AccessibleStatus))]
    private PhotoInsight _insight = PhotoInsight.NotAnalysed;

    /// <summary>Optional AI findings (advisory), set by <see cref="Services.AnalysisController"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaceBadgeText), nameof(ShowAiPickBadge), nameof(ShowFacesSoftBadge), nameof(HasAnalysisBadges), nameof(AccessibleStatus))]
    private AiInsight _ai = AiInsight.None;

    public string? FaceBadgeText => Ai.HasFaces ? string.Create(CultureInfo.CurrentCulture, $"☺ {Ai.FaceCount}") : null;

    public bool ShowAiPickBadge => Ai.IsSuggestedPick;

    public bool ShowFacesSoftBadge => Ai.FacesSoft;

    public bool ShowBlurBadge => Insight.LikelyBlurry;

    public bool ShowClipBadge => Insight.HighlightsClipped || Insight.ShadowsClipped;

    public string? GroupBadgeText => Insight.GroupKey is null ? null : string.Create(CultureInfo.CurrentCulture, $"▣ {Insight.GroupSize}");

    public bool ShowExactBadge => Insight.ExactDuplicateCount > 0;

    public bool ShowSharpestBadge => Insight.IsSharpestInGroup;

    public bool HasAnalysisBadges => ShowBlurBadge || ShowClipBadge || GroupBadgeText is not null || ShowExactBadge
        || FaceBadgeText is not null || ShowAiPickBadge || ShowFacesSoftBadge;

    /// <summary>✓ Keep, ? Review, ✕ Reject; null when unset.</summary>
    public string? StateGlyph => Decision.State switch
    {
        CullState.Keep => "✓",
        CullState.Review => "?",
        CullState.Reject => "✕",
        _ => null,
    };

    public string? StateText => Decision.State == CullState.Unset ? null : Decision.State.ToString();

    public bool IsRejected => Decision.State == CullState.Reject;

    public string? RatingText => Decision.Rating is { } r ? new string('★', r) : null;

    public ColourLabel Label => Decision.Label;

    /// <summary>Letter shown on the label swatch, so the label is not conveyed by colour alone.</summary>
    public string? LabelLetter => Decision.Label == ColourLabel.None ? null : Decision.Label.ToString()[..1];

    /// <summary>Spoken by screen readers: problem state plus decision, e.g. "Keep, 3 stars, Red label".</summary>
    public string AccessibleStatus
    {
        get
        {
            var parts = new List<string>(4);
            if (StatusText is { } problem)
            {
                parts.Add(problem);
            }

            if (StateText is { } state)
            {
                parts.Add(state);
            }

            if (Decision.Rating is { } r)
            {
                parts.Add(r == 1 ? "1 star" : string.Create(CultureInfo.InvariantCulture, $"{r} stars"));
            }

            if (Decision.Label != ColourLabel.None)
            {
                parts.Add($"{Decision.Label} label");
            }

            if (Insight.LikelyBlurry)
            {
                parts.Add("likely blurry");
            }

            if (ShowClipBadge)
            {
                parts.Add("clipping");
            }

            if (Insight.GroupKey is not null)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"in a group of {Insight.GroupSize} similar photos"));
            }

            if (Insight.IsSharpestInGroup)
            {
                parts.Add("sharpest in group");
            }

            if (Insight.ExactDuplicateCount > 0)
            {
                parts.Add("exact duplicate");
            }

            if (Ai.HasFaces)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"AI: {Ai.FaceCount} face(s)"));
            }

            if (Ai.FacesSoft)
            {
                parts.Add("AI: faces soft");
            }

            if (Ai.IsSuggestedPick)
            {
                parts.Add($"AI suggested pick, {Ai.PickConfidence} confidence");
            }

            if (IsEdited)
            {
                parts.Add("edited");
            }

            if (IsPaired)
            {
                parts.Add("RAW + JPEG");
            }

            return string.Join(", ", parts);
        }
    }

    public Photo Photo { get; }

    /// <summary>
    /// The RAW file shown through this tile when it is the cover (JPEG) of a RAW + JPEG pair (M09); null otherwise. The
    /// partner has its own tile object, never shown, that holds its decision.
    /// </summary>
    public PhotoTileViewModel? Partner { get; init; }

    public bool IsPaired => Partner is not null;

    /// <summary>Tooltip of the RAW+JPEG mark.</summary>
    public string? PairToolTip => Partner is { } raw
        ? $"RAW + JPEG: {raw.FileName} and {FileName} are shown and decided on as one photo."
        : null;

    public Guid Id => Photo.Id;

    public string FileName => Photo.FileName;

    public string FilePath => Photo.FilePath;

    public ImportStatus Status => Photo.Status;

    /// <summary>Short label for problem states; null when the photo is fine.</summary>
    public string? StatusText => OfflineReason is not null ? "Offline" : Photo.Status switch
    {
        ImportStatus.Missing => "Missing",
        ImportStatus.Failed => "Unreadable",
        _ => ThumbnailError,
    };

    public bool HasProblem => StatusText is not null;

    /// <summary>Why the photo's folder can't be reached right now (drive unplugged, folder moved), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasProblem), nameof(AccessibleStatus), nameof(ToolTipText))]
    private string? _offlineReason;

    public string ToolTipText => (OfflineReason ?? Photo.LastError) is { } error ? $"{Photo.FilePath}\n{error}" : Photo.FilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasProblem), nameof(AccessibleStatus))]
    private string? _thumbnailError;

    /// <summary>The photo's non-destructive edit, set by <see cref="Services.EditController"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdited), nameof(Thumbnail), nameof(AccessibleStatus))]
    private EditRecipe _recipe = EditRecipe.Neutral;

    public bool IsEdited => !Recipe.IsNeutral;

    /// <summary>Changed by the last Auto batch (for the review filter). Not stored: cleared on restart, undo or the next batch.</summary>
    [ObservableProperty]
    private bool _autoApplied;

    public BitmapSource? Thumbnail
    {
        get
        {
            // A thumbnail rendered for an older recipe is shown until the current one arrives.
            var cached = _thumbnails.TryGetCached(Id, Recipe, out var isCurrent);
            if (!isCurrent && !_requested && ThumbnailError is null && Photo.Status == ImportStatus.Imported)
            {
                _requested = true;
                _thumbnails.Request(this);
            }

            return cached;
        }
    }

    /// <summary>One-line metadata summary for the viewer.</summary>
    public string DetailsText => _details ??= IsPaired ? "RAW + JPEG  ·  " + BuildDetails(Photo) : BuildDetails(Photo);

    void IThumbnailRequester.OnThumbnailLoaded()
    {
        _requested = false;
        OnPropertyChanged(nameof(Thumbnail));
    }

    void IThumbnailRequester.OnThumbnailFailed(string message)
    {
        _requested = false;
        ThumbnailError = message;
    }

    void IThumbnailRequester.OnThumbnailRequestDropped() => _requested = false;

    internal static string BuildDetails(Photo photo)
    {
        var culture = CultureInfo.CurrentCulture;
        var parts = new List<string>();

        if (photo.Width is { } w && photo.Height is { } h)
        {
            // Show dimensions as displayed (after EXIF rotation), not as stored.
            if (ExifOrientation.SwapsDimensions(photo.Orientation))
            {
                (w, h) = (h, w);
            }

            parts.Add(string.Create(culture, $"{w} × {h}"));
        }

        var camera = CameraName.Format(photo.CameraMake, photo.CameraModel);
        if (camera.Length > 0)
        {
            parts.Add(camera);
        }

        if (!string.IsNullOrWhiteSpace(photo.LensModel))
        {
            parts.Add(photo.LensModel);
        }

        if (photo.Iso is { } iso)
        {
            parts.Add(string.Create(culture, $"ISO {iso}"));
        }

        if (photo.ExposureTime is { } exposure)
        {
            parts.Add(exposure + " s");
        }

        if (photo.FNumber is { } f)
        {
            parts.Add(string.Create(culture, $"f/{f:0.#}"));
        }

        if (photo.FocalLengthMm is { } focal)
        {
            parts.Add(string.Create(culture, $"{focal:0.#} mm"));
        }

        if (photo.CaptureLocalTime is { } taken)
        {
            parts.Add(taken.ToString("g", culture));
        }

        return parts.Count == 0 ? "No metadata" : string.Join("  ·  ", parts);
    }
}
