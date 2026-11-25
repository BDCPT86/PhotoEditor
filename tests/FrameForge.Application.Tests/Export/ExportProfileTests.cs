using FrameForge.Application.Editing;
using FrameForge.Application.Export;
using FrameForge.Application.Settings;
using FrameForge.Application.Tests.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Application.Tests.Export;

public sealed class ExportProfileTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExportProfile Mine(string name = "Mine", int quality = 80) => new()
    {
        Name = name,
        Mode = ExportMode.EditedJpegs,
        Quality = quality,
        NamePattern = "{date}_{name}",
    };

    [Fact]
    public void BuiltIns_AreValid_AndHaveTheAgreedSettings()
    {
        Assert.All(ExportProfiles.BuiltIn, p => Assert.Empty(p.Validate()));
        Assert.Equal(ExportMode.EditedJpegs, ExportProfiles.FullSizeJpeg.Mode);
        Assert.Null(ExportProfiles.FullSizeJpeg.MaxLongSide);
        Assert.Equal(ExportMetadata.All, ExportProfiles.FullSizeJpeg.Metadata);
        Assert.Equal(2048, ExportProfiles.Web.MaxLongSide);
        Assert.Equal(ExportMetadata.AllButLocation, ExportProfiles.Web.Metadata);
        Assert.Equal(ExportMode.CopyFiles, ExportProfiles.Originals.Mode);
        Assert.Equal(string.Empty, ExportProfiles.Originals.NamePattern);
    }

    [Theory]
    [InlineData("", 90, null, "{name}", "needs a name")]
    [InlineData("ok", 40, null, "{name}", "quality")]
    [InlineData("ok", 90, 10, "{name}", "longest side")]
    [InlineData("ok", 90, null, "{nope}", "file-name pattern")]
    public void Validate_NamesTheProblem(string name, int quality, int? maxSide, string pattern, string expected)
    {
        var profile = new ExportProfile { Name = name, Quality = quality, MaxLongSide = maxSide, NamePattern = pattern };

        Assert.Contains(profile.Validate(), e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsLongNames_AndUnknownEnums()
    {
        Assert.NotEmpty(Mine(new string('x', ExportProfile.MaxNameLength + 1)).Validate());
        Assert.Empty(Mine(new string('x', ExportProfile.MaxNameLength)).Validate());
        Assert.NotEmpty((Mine() with { Mode = (ExportMode)42 }).Validate());
        Assert.NotEmpty((Mine() with { Metadata = (ExportMetadata)42 }).Validate());
    }

    [Fact]
    public void HasSameSettings_IgnoresTheName_ButNotAnySetting()
    {
        Assert.True(Mine("a").HasSameSettings(Mine("b")));
        Assert.False(Mine().HasSameSettings(Mine() with { WriteRatingAndLabel = false }));
        Assert.False(Mine().HasSameSettings(Mine() with { MaxLongSide = 2000 }));
    }

    [Fact]
    public void Save_TrimsTheName_ReplacesIgnoringCase_AndKeepsTheOrder()
    {
        var set = ExportProfiles.Save(ExportProfileSet.Empty, Mine("  First "));
        set = ExportProfiles.Save(set, Mine("Second"));
        set = ExportProfiles.Save(set, Mine("FIRST", quality: 70));

        Assert.Equal(["FIRST", "Second"], set.Profiles.Select(p => p.Name));
        Assert.Equal(70, set.Profiles[0].Quality);
        Assert.True(set.IsValid());
    }

    [Theory]
    [InlineData("Originals")]
    [InlineData(" full-size jpeg ")]
    public void Save_RejectsBuiltInNames(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => ExportProfiles.Save(ExportProfileSet.Empty, Mine(name)));
        Assert.Contains("built-in", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RejectsAnInvalidProfile()
    {
        Assert.Throws<ArgumentException>(() => ExportProfiles.Save(ExportProfileSet.Empty, Mine() with { Quality = 5 }));
    }

    [Fact]
    public void Delete_RemovesOnlyThatName()
    {
        var set = ExportProfiles.Save(ExportProfiles.Save(ExportProfileSet.Empty, Mine("A")), Mine("B"));

        Assert.Equal(["B"], ExportProfiles.Delete(set, " a ").Profiles.Select(p => p.Name));
        Assert.Equal(2, ExportProfiles.Delete(set, "Originals").Profiles.Count);
    }

    [Fact]
    public void IsValid_RejectsOtherVersions_DuplicateNames_AndBuiltInNames()
    {
        Assert.True(ExportProfileSet.Empty.IsValid());
        Assert.False((ExportProfileSet.Empty with { Version = 2 }).IsValid());
        Assert.False(new ExportProfileSet { Profiles = [Mine("A"), Mine("a")] }.IsValid());
        Assert.False(new ExportProfileSet { Profiles = [Mine("Originals")] }.IsValid());
        Assert.False(new ExportProfileSet { LastUsed = Mine() with { Quality = 1 } }.IsValid());
    }

    // ---------------------------------------------------------------- stored as JSON

    private static (ExportProfileService Service, InMemorySettingsStore Store) Service()
    {
        var store = new InMemorySettingsStore();
        return (new ExportProfileService(new SettingsService(store, NullLogger<SettingsService>.Instance)), store);
    }

    [Fact]
    public async Task Stored_ProfilesAndLastUsed_RoundTrip()
    {
        var (service, store) = Service();
        await service.SaveProfileAsync(Mine("A"), Ct);
        await service.SaveProfileAsync(Mine("B") with { MaxLongSide = 1600, Metadata = ExportMetadata.None }, Ct);
        await service.SaveLastUsedAsync(ExportProfiles.Web, Ct);

        var reloaded = await new ExportProfileService(new SettingsService(store, NullLogger<SettingsService>.Instance)).LoadAsync(Ct);

        Assert.Equal(["A", "B"], reloaded.Profiles.Select(p => p.Name));
        Assert.Equal(Mine("B") with { MaxLongSide = 1600, Metadata = ExportMetadata.None }, reloaded.Profiles[1]);
        Assert.Equal(ExportProfiles.Web, reloaded.LastUsed);
        Assert.Contains("\"Version\":1", store.Values[ExportProfileService.Setting.Key], StringComparison.Ordinal);
        Assert.Contains("\"AllButLocation\"", store.Values[ExportProfileService.Setting.Key], StringComparison.Ordinal); // enums by name
    }

    [Fact]
    public async Task SavingLastUsed_KeepsTheProfiles_AndDeletingKeepsLastUsed()
    {
        var (service, _) = Service();
        await service.SaveProfileAsync(Mine("A"), Ct);
        await service.SaveLastUsedAsync(Mine("ignored"), Ct);
        var set = await service.DeleteProfileAsync("A", Ct);

        Assert.Empty(set.Profiles);
        Assert.NotNull((await service.LoadAsync(Ct)).LastUsed);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Version\":2,\"Profiles\":[]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[{\"Name\":\"Originals\"}]}")]
    public async Task UnreadableOrFutureData_LoadsAsEmpty(string json)
    {
        var (service, store) = Service();
        store.Values[ExportProfileService.Setting.Key] = json;

        var set = await service.LoadAsync(Ct);

        Assert.Empty(set.Profiles);
        Assert.Null(set.LastUsed);
    }

    [Fact]
    public async Task SaveLastUsed_RejectsInvalidSettings()
    {
        var (service, _) = Service();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveLastUsedAsync(Mine() with { NamePattern = "{x" }, Ct));
    }
}
