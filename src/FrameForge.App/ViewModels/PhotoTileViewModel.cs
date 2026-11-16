using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.Services;
using FrameForge.Core.Decisions;
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

            return string.Join(", ", parts);
        }
    }

    public Photo Photo { get; }

    public Guid Id => Photo.Id;

    public string FileName => Photo.FileName;

    public string FilePath => Photo.FilePath;

    public ImportStatus Status => Photo.Status;

    /// <summary>Short label for problem states; null when the photo is fine.</summary>
    public string? StatusText => Photo.Status switch
    {
        ImportStatus.Missing => "Missing",
        ImportStatus.Failed => "Unreadable",
        _ => ThumbnailError,
    };

    public bool HasProblem => StatusText is not null;

    public string ToolTipText => Photo.LastError is { } error ? $"{Photo.FilePath}\n{error}" : Photo.FilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasProblem), nameof(AccessibleStatus))]
    private string? _thumbnailError;

    public BitmapSource? Thumbnail
    {
        get
        {
            var cached = _thumbnails.TryGetCached(Id);
            if (cached is null && !_requested && ThumbnailError is null && Photo.Status == ImportStatus.Imported)
            {
                _requested = true;
                _thumbnails.Request(this);
            }

            return cached;
        }
    }

    /// <summary>One-line metadata summary for the viewer.</summary>
    public string DetailsText => _details ??= BuildDetails(Photo);

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

        var camera = string.Join(' ', new[] { photo.CameraMake, photo.CameraModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
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
