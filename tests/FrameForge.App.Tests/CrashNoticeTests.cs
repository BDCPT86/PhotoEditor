using FrameForge.App.Services;
using FrameForge.App.ViewModels;
using FrameForge.Application.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

public sealed class CrashNoticeTests
{
    private sealed class Reports(params string[] unseen) : ICrashReports
    {
        public List<string> Pending { get; } = [.. unseen];

        public IReadOnlyList<string> Unseen() => Pending.ToList();

        public void MarkSeen() => Pending.Clear();

        public string? Read(string path) => "report text of " + path;
    }

    private sealed class Desktop : IDesktopShell
    {
        public List<string> Shown { get; } = [];

        public List<string> Copied { get; } = [];

        public void ShowInFolder(string path) => Shown.Add(path);

        public void CopyToClipboard(string text) => Copied.Add(text);
    }

    [Fact]
    public void NoReports_NoNotice()
    {
        var notice = new CrashNoticeViewModel(new Reports(), new Desktop(), NullLogger<CrashNoticeViewModel>.Instance);

        notice.Check();

        Assert.False(notice.IsShown);
    }

    [Fact]
    public void Reports_ShowANotice_ThatOpensAndCopiesTheNewest_AndGoesAwayOnDismiss()
    {
        var reports = new Reports(@"C:\r\crash-2.txt", @"C:\r\crash-1.txt");
        var desktop = new Desktop();
        var notice = new CrashNoticeViewModel(reports, desktop, NullLogger<CrashNoticeViewModel>.Instance);

        notice.Check();
        Assert.True(notice.IsShown);
        Assert.Contains("2 problems", notice.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent", notice.Message, StringComparison.Ordinal);

        notice.ShowReportCommand.Execute(null);
        notice.CopyReportCommand.Execute(null);
        Assert.Equal([@"C:\r\crash-2.txt"], desktop.Shown);
        Assert.Equal([@"report text of C:\r\crash-2.txt"], desktop.Copied);

        notice.DismissCommand.Execute(null);
        Assert.False(notice.IsShown);
        Assert.Empty(reports.Pending);
    }
}
