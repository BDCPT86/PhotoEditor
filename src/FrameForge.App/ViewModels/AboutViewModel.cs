using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameForge.App.Startup;
using Microsoft.Extensions.Logging;

namespace FrameForge.App.ViewModels;

public sealed partial class AboutViewModel(AppInfo appInfo, ILogger<AboutViewModel> logger) : PageViewModel
{
    internal const string NoticesFileName = "THIRD-PARTY-NOTICES.md";

    public override string Title => "About";

    public AppInfo Info { get; } = appInfo;

    /// <summary>The full licence and notice texts shipped next to the app (see licenses/README.md).</summary>
    public string LicensesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "licenses");

    /// <summary>Third-party licences and model attributions, read from the file shipped next to the app.</summary>
    [ObservableProperty]
    private string _notices = string.Empty;

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (Notices.Length > 0)
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, NoticesFileName);
        try
        {
            Notices = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogNoticesMissing(ex, path);
            Notices = "Third-party notices could not be read (" + ex.Message + "). See " + NoticesFileName + " in the source repository.";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Third-party notices file could not be read: {Path}")]
    private partial void LogNoticesMissing(Exception exception, string path);
}
