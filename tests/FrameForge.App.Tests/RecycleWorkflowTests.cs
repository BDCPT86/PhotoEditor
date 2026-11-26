using System.IO;
using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Cleanup;
using FrameForge.Core.Decisions;
using FrameForge.Core.Library;

namespace FrameForge.App.Tests;

/// <summary>M09 B2: the "Move rejects to Recycle Bin" command and its confirmation panel. The Recycle Bin is a fake.</summary>
public sealed class RecycleWorkflowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>IMG_00 and IMG_02 rejected (IMG_02 is a RAW + JPEG pair), IMG_01 kept; the Rejected view shown.</summary>
    private static async Task<CullingFixture> RejectedViewAsync(int photoCount = 4)
    {
        var f = new CullingFixture(photoCount, pairedRaws: [2]);
        await f.Decisions.SaveAsync(
            [
                (f.Photos[0].Id, new PhotoDecision(CullState.Reject)),
                (f.Photos[1].Id, new PhotoDecision(CullState.Keep)),
                (f.Photos[2].Id, new PhotoDecision(CullState.Reject)),
                (f.Raws[2].Id, new PhotoDecision(CullState.Reject)),
            ],
            Ct);
        await f.LoadAsync();
        ShowRejected(f);
        return f;
    }

    private static void ShowRejected(CullingFixture f) =>
        f.Library.Filter.SelectedScope = f.Library.Filter.Scopes.First(s => s.Scope == LibraryScope.Rejected);

    private static async Task OpenAsync(CullingFixture f)
    {
        Assert.True(f.Library.TryHandle(CullingAction.RecycleRejects));
        for (var i = 0; i < 100 && f.Recycle.IsChecking; i++)
        {
            await Task.Delay(10, Ct);
        }
    }

    [Fact]
    public async Task TheCommand_IsOnlyOffered_InTheRejectedView()
    {
        using var f = new CullingFixture(photoCount: 2);
        await f.LoadAsync();

        Assert.False(f.Library.CanRecycleRejects);
        f.Library.TryHandle(CullingAction.RecycleRejects);

        Assert.False(f.Recycle.IsOpen);
        Assert.Contains("Rejected view", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyRejectedView_OffersNothing()
    {
        using var f = new CullingFixture(photoCount: 2);
        await f.LoadAsync();
        ShowRejected(f);

        Assert.False(f.Library.CanRecycleRejects);
        f.Library.TryHandle(CullingAction.RecycleRejects);
        Assert.False(f.Recycle.IsOpen);
    }

    [Fact]
    public async Task Opening_ChecksBothFilesOfAPair_ShowsWhatWillMove_AndMovesNothing()
    {
        using var f = await RejectedViewAsync();
        Assert.True(f.Library.CanRecycleRejects);

        await OpenAsync(f);

        Assert.True(f.Recycle.IsOpen);
        Assert.Equal(["IMG_00.jpg", "IMG_02.jpg", "IMG_02.CR3"], Assert.Single(f.RecycleBin.Checked).Select(Path.GetFileName));
        Assert.Equal("2 photos  ·  3 files  ·  75 MB", f.Recycle.SummaryText);
        Assert.Equal("Move 3 files to Recycle Bin", f.Recycle.ConfirmText);
        var folder = Assert.Single(f.Recycle.Folders);
        Assert.StartsWith("Shoot", folder.Text, StringComparison.Ordinal);
        Assert.Equal(@"C:\Shoot", folder.Hint);
        Assert.False(f.Recycle.HasSkipped);
        Assert.Empty(f.RecycleBin.Moves);
    }

    [Fact]
    public async Task ChoosingAFolder_NarrowsTheRejects()
    {
        var f = new CullingFixture(photoCount: 3);
        try
        {
            var other = f.Photos[2];
            other.FolderPath = @"C:\Other";
            other.FilePath = @"C:\Other\IMG_02.jpg";
            await f.Decisions.SaveAsync([(f.Photos[0].Id, new PhotoDecision(CullState.Reject)), (other.Id, new PhotoDecision(CullState.Reject))], Ct);
            await f.LoadAsync();
            ShowRejected(f);
            f.Library.Filter.Folder = f.Library.Filter.Folders.First(x => x.Path == @"C:\Other");

            await OpenAsync(f);

            Assert.Equal([@"C:\Other\IMG_02.jpg"], Assert.Single(f.RecycleBin.Checked));
        }
        finally
        {
            f.Dispose();
        }
    }

    [Fact]
    public async Task SkippedFiles_AreListedByReason_AndOnlyTheRestMove()
    {
        using var f = await RejectedViewAsync();
        f.RecycleBin.Refused[@"C:\Shoot\IMG_00.jpg"] = RecycleRefusal.NoRecycleBinOnDrive;
        await OpenAsync(f);

        var skipped = Assert.Single(f.Recycle.Skipped);
        Assert.Equal("1 file on a drive without a Recycle Bin (memory card, USB drive or network)", skipped.Text);
        Assert.Equal("IMG_00.jpg", skipped.Hint);
        Assert.Equal("1 photo  ·  2 files  ·  50 MB", f.Recycle.SummaryText);
        Assert.Equal("Move 2 files to Recycle Bin", f.Recycle.ConfirmText);

        await f.Recycle.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\Shoot\IMG_02.jpg", @"C:\Shoot\IMG_02.CR3"], Assert.Single(f.RecycleBin.Moves));
        Assert.False(f.Recycle.IsOpen);
        Assert.StartsWith("Moved 2 files to the Recycle Bin.", f.Library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenNothingCanMove_TheButtonIsOff_AndThePanelSaysWhy()
    {
        using var f = await RejectedViewAsync();
        foreach (var path in new[] { @"C:\Shoot\IMG_00.jpg", @"C:\Shoot\IMG_02.jpg", @"C:\Shoot\IMG_02.CR3" })
        {
            f.RecycleBin.Refused[path] = RecycleRefusal.RecycleBinOff;
        }

        await OpenAsync(f);

        Assert.False(f.Recycle.ConfirmCommand.CanExecute(null));
        Assert.Null(f.Recycle.SummaryText);
        Assert.Contains("nothing will move", f.Recycle.Message, StringComparison.Ordinal);
        Assert.Equal("3 files on a drive with its Recycle Bin turned off", Assert.Single(f.Recycle.Skipped).Text);
    }

    [Fact]
    public async Task Closing_MovesNothing()
    {
        using var f = await RejectedViewAsync();
        await OpenAsync(f);

        f.Recycle.CloseCommand.Execute(null);

        Assert.False(f.Recycle.IsOpen);
        Assert.Empty(f.RecycleBin.Moves);
    }

    [Fact]
    public async Task InTheViewer_DeleteDoesNothing()
    {
        using var f = await RejectedViewAsync();
        f.Library.OpenViewerCommand.Execute(f.Library.Photos[0]);
        Assert.True(f.Library.IsViewerOpen);

        f.Library.TryHandle(CullingAction.RecycleRejects);

        Assert.False(f.Recycle.IsOpen);
    }

    [Fact]
    public void DeleteKey_OpensIt_AndHelpListsIt()
    {
        Assert.Equal(CullingAction.RecycleRejects, CullingShortcuts.Resolve(System.Windows.Input.Key.Delete, System.Windows.Input.ModifierKeys.None));
        Assert.Contains(CullingShortcuts.Help.SelectMany(g => g.Items), i => i.Keys == "Delete");
    }

    [Theory]
    [InlineData(512L, "1 KB")]
    [InlineData(25L << 20, "25 MB")]
    [InlineData(3L << 30, "3{0}0 GB")]
    public void Sizes_ReadNaturally(long bytes, string expected) =>
        Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, expected, System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), RecycleRejectsViewModel.FormatSize(bytes));
}
