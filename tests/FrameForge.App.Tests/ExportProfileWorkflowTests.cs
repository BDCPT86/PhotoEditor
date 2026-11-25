using FrameForge.App.Shortcuts;
using FrameForge.App.ViewModels;
using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Core.Decisions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.App.Tests;

public sealed class ExportProfileWorkflowTests
{
    private static async Task<CullingFixture> OpenExportAsync()
    {
        var f = new CullingFixture();
        await f.LoadAsync();
        f.Select(0);
        f.Library.TryHandle(CullingAction.Keep);
        await f.StoredAsync(0, d => d.State == CullState.Keep);
        f.Library.OpenExportCommand.Execute(null);
        await f.Export.ProfilesLoaded;
        return f;
    }

    [Fact]
    public async Task FirstOpen_ShowsTheBuiltIns_AndTheDefaultsMatchOriginals()
    {
        using var f = await OpenExportAsync();

        Assert.Equal(["Full-size JPEG", "Web (2048 px, no location)", "Originals"], f.Export.Profiles.Select(p => p.Name));
        Assert.Equal("Originals", f.Export.SelectedProfile?.Name);
        Assert.False(f.Export.ProfileIsChanged);
        Assert.False(f.Export.CanDeleteProfile);
    }

    [Fact]
    public async Task ChoosingAProfile_FillsInItsSettings_AndChangingOneMarksItChanged()
    {
        using var f = await OpenExportAsync();

        f.Export.SelectedProfile = ExportProfiles.Web;

        Assert.Equal(ExportMode.EditedJpegs, f.Export.Mode);
        Assert.Equal(85, f.Export.Quality);
        Assert.Equal("2048", f.Export.MaxLongSideText);
        Assert.Equal(ExportMetadata.AllButLocation, f.Export.Metadata);
        Assert.False(f.Export.WriteRatingAndLabel);
        Assert.False(f.Export.ProfileIsChanged);

        f.Export.Quality = 80;
        Assert.True(f.Export.ProfileIsChanged);
        Assert.Equal("Web (2048 px, no location)", f.Export.SelectedProfile?.Name); // still the starting point

        f.Export.Quality = 85;
        Assert.False(f.Export.ProfileIsChanged);
    }

    [Fact]
    public async Task SaveProfile_AddsIt_SelectsIt_AndAReplacementKeepsOneCopy()
    {
        using var f = await OpenExportAsync();
        f.Export.Mode = ExportMode.EditedJpegs;
        f.Export.NameTemplateText = "{date}_{name}";
        f.Export.NewProfileName = "  Club prints ";

        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        Assert.Null(f.Export.ProfileMessage);
        Assert.Equal("Club prints", f.Export.SelectedProfile?.Name);
        Assert.Equal(string.Empty, f.Export.NewProfileName);
        Assert.True(f.Export.CanDeleteProfile);

        f.Export.Quality = 70;
        f.Export.NewProfileName = "CLUB PRINTS";
        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        var saved = Assert.Single(f.Export.Profiles, p => p.Name.Equals("club prints", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(70, saved.Quality);
        Assert.Equal(4, f.Export.Profiles.Count);
    }

    [Fact]
    public async Task SaveAs_OpensTheNameField_SuggestsTheUsersOwnName_AndClosesOnSaveOrCancel()
    {
        using var f = await OpenExportAsync();

        f.Export.BeginNamingProfileCommand.Execute(null);
        Assert.True(f.Export.IsNamingProfile);
        Assert.Equal(string.Empty, f.Export.NewProfileName); // a built-in is selected: no suggestion

        f.Export.NewProfileName = "Mine";
        f.Export.Quality = 77;
        f.Export.Mode = ExportMode.EditedJpegs;
        await f.Export.SaveProfileCommand.ExecuteAsync(null);
        Assert.False(f.Export.IsNamingProfile);

        f.Export.BeginNamingProfileCommand.Execute(null);
        Assert.Equal("Mine", f.Export.NewProfileName); // updating your own profile is one Enter away
        f.Export.CancelNamingProfileCommand.Execute(null);
        Assert.False(f.Export.IsNamingProfile);
        Assert.Equal(string.Empty, f.Export.NewProfileName);
    }

    [Fact]
    public async Task SaveProfile_WithABuiltInName_OrABadPattern_SaysWhy_AndSavesNothing()
    {
        using var f = await OpenExportAsync();
        f.Export.NewProfileName = "originals";

        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        Assert.Contains("built-in", f.Export.ProfileMessage, StringComparison.Ordinal);

        f.Export.NameTemplateText = "{stars}";
        f.Export.NewProfileName = "Mine";
        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        Assert.Contains("file-name pattern", f.Export.ProfileMessage, StringComparison.Ordinal);
        Assert.Equal(3, f.Export.Profiles.Count);
    }

    [Fact]
    public async Task DeleteProfile_RemovesOnlyTheUsersProfile()
    {
        using var f = await OpenExportAsync();
        f.Export.Quality = 75;
        f.Export.Mode = ExportMode.EditedJpegs;
        f.Export.NewProfileName = "Proofs";
        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        await f.Export.DeleteProfileCommand.ExecuteAsync(null);

        Assert.DoesNotContain(f.Export.Profiles, p => p.Name == "Proofs");
        Assert.Equal(3, f.Export.Profiles.Count);
        Assert.Null(f.Export.SelectedProfile); // the settings no longer match any profile
        Assert.Equal(75, f.Export.Quality); // removing a profile doesn't change the settings

        f.Export.SelectedProfile = ExportProfiles.Originals;
        Assert.False(f.Export.DeleteProfileCommand.CanExecute(null));
    }

    [Fact]
    public async Task LastExportsSettings_AreRestoredNextTime_ButNotTheDestination()
    {
        using var f = await OpenExportAsync();
        f.Export.SelectedProfile = ExportProfiles.Web;
        f.Export.NameTemplateText = "{seq:4}";
        f.FolderPicker.Next = @"D:\Out";
        await f.Export.StartCommand.ExecuteAsync(null);
        Assert.Single(f.EditedExporter.Calls);

        // A new panel on the same settings store, as after restarting the app.
        var next = new ExportViewModel(f.Exporter, f.EditedExporter, f.FolderPicker, new NoSavePicker(), new ExportProfileService(f.Settings), NullLogger<ExportViewModel>.Instance);
        next.Open(_ => [], []);
        await next.ProfilesLoaded;

        Assert.Equal(ExportMode.EditedJpegs, next.Mode);
        Assert.Equal("{seq:4}", next.NameTemplateText);
        Assert.Equal("2048", next.MaxLongSideText);
        Assert.Equal(ExportMetadata.AllButLocation, next.Metadata);
        Assert.Null(next.SelectedProfile); // the pattern differs from the Web profile
        Assert.Null(next.PendingDestination);
    }

    [Fact]
    public async Task ProfilesSavedInOnePanel_AreListedInTheNext()
    {
        using var f = await OpenExportAsync();
        f.Export.NewProfileName = "Dated copies";
        f.Export.NameTemplateText = "{date}_{name}";
        await f.Export.SaveProfileCommand.ExecuteAsync(null);

        var next = new ExportViewModel(f.Exporter, f.EditedExporter, f.FolderPicker, new NoSavePicker(), new ExportProfileService(f.Settings), NullLogger<ExportViewModel>.Instance);
        next.Open(_ => [], []);
        await next.ProfilesLoaded;

        Assert.Contains(next.Profiles, p => p.Name == "Dated copies" && p.NamePattern == "{date}_{name}");
    }

    private sealed class NoSavePicker : FrameForge.App.Services.IFileSavePicker
    {
        public string? PickSaveFile(string title, string defaultFileName, string filter) => null;
    }
}
