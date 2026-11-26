using System.Globalization;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.Analysis;
using FrameForge.Imaging;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

/// <summary>
/// The histogram of the image on screen (M09 C1). Counting runs off the UI thread; while one count runs, newer images
/// replace each other, so dragging a slider updates it as fast as it can without queueing stale work.
/// </summary>
public sealed partial class HistogramViewModel(ILogger logger) : ObservableObject
{
    private object? _current;
    private object? _pending;
    private bool _running;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShadowText), nameof(HighlightText), nameof(AccessibleText))]
    private ImageHistogram? _histogram;

    /// <summary>"Shadows 0.2 %": pixels with every channel at 0–1.</summary>
    public string? ShadowText => Histogram is { } h ? "Shadows " + Share(h.ShadowClippedPercent) : null;

    /// <summary>"Highlights 1.4 %": pixels with any channel at 254–255.</summary>
    public string? HighlightText => Histogram is { } h ? "Highlights " + Share(h.HighlightClippedPercent) : null;

    /// <summary>What a screen reader says for the histogram, which is otherwise only a picture.</summary>
    public string AccessibleText => Histogram is { } h
        ? string.Create(CultureInfo.CurrentCulture, $"Histogram. Clipped: shadows {Share(h.ShadowClippedPercent)}, highlights {Share(h.HighlightClippedPercent)}.")
        : "Histogram: no image";

    /// <summary>The count in progress, if any (for tests).</summary>
    internal Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>Counts this image: rendered pixels (<see cref="AnalysisImage"/>) or a frozen bitmap. Null clears it.</summary>
    public void Show(object? image)
    {
        if (ReferenceEquals(image, _current))
        {
            return;
        }

        _current = image;
        if (image is null)
        {
            _pending = null;
            Histogram = null;
            return;
        }

        _pending = image;
        if (!_running)
        {
            Work = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            while (_pending is { } image)
            {
                _pending = null;
                ImageHistogram? counted;
                try
                {
                    counted = await Task.Run(() => Count(image)).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    LogCountFailed(ex);
                    counted = null;
                }

                if (ReferenceEquals(image, _current))
                {
                    Histogram = counted;
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    private static ImageHistogram Count(object image)
    {
        var pixels = image switch
        {
            AnalysisImage rendered => rendered,
            BitmapSource bitmap => BitmapConversions.ToBuffer(bitmap), // frozen, so readable from this thread
            _ => throw new NotSupportedException($"No histogram for {image.GetType().Name}."),
        };
        return ImageHistogram.Compute(pixels.Bgra, pixels.Width, pixels.Height, pixels.Width * 4);
    }

    /// <summary>"0 %", "&lt; 0.1 %", "1.4 %", "12 %".</summary>
    internal static string Share(double percent) => percent switch
    {
        <= 0 => "0 %",
        < 0.1 => string.Create(CultureInfo.CurrentCulture, $"< {0.1:0.0} %"),
        < 10 => string.Create(CultureInfo.CurrentCulture, $"{percent:0.0} %"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{percent:0} %"),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "The histogram could not be counted for the image on screen.")]
    private partial void LogCountFailed(Exception exception);
}
