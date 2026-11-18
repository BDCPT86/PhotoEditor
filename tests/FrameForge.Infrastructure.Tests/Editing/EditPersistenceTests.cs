using FrameForge.Application.Editing;
using FrameForge.Application.Photos;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Infrastructure.Persistence;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FrameForge.Infrastructure.Tests.Editing;

public sealed class EditPersistenceTests : IDisposable
{
    private static readonly EditRecipe Sample = EditRecipe.Neutral with
    {
        Exposure = 0.67,
        Temperature = -12.5,
        Tint = 3,
        Contrast = 25,
        Highlights = -40,
        Shadows = 35,
        Saturation = 10,
        Vibrance = 20,
        QuarterTurns = 3,
        Straighten = -2.25,
        Crop = new CropRect(0.05, 0.1, 0.8, 0.75),
    };

    private readonly TempDirectory _temp = new();
    private readonly AppDataPaths _paths;

    public EditPersistenceTests()
    {
        _paths = new AppDataPaths(_temp.Path);
        _paths.EnsureDirectoriesExist();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private static async Task<Photo> AddPhotoAsync(IServiceProvider services, string name)
    {
        var photo = new Photo { Id = Guid.NewGuid(), FilePath = $@"C:\Shoot\{name}", FileName = name, FolderPath = @"C:\Shoot", Status = ImportStatus.Imported };
        await services.GetRequiredService<IPhotoRepository>().UpsertAsync([photo], Ct);
        return photo;
    }

    [Fact]
    public async Task Recipe_RoundTripsExactly_AcrossRestart()
    {
        Guid id;
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            id = (await AddPhotoAsync(first, "a.jpg")).Id;
            await first.GetRequiredService<IEditService>().SaveAsync([(id, Sample)], Ct);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);
        var loaded = await second.GetRequiredService<IEditService>().LoadAsync(Ct);

        Assert.Equal(Sample, loaded[id]);
    }

    [Fact]
    public async Task NeutralRecipe_DeletesTheRow()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var id = (await AddPhotoAsync(services, "a.jpg")).Id;
        var edits = services.GetRequiredService<IEditService>();

        await edits.SaveAsync([(id, Sample)], Ct);
        await edits.SaveAsync([(id, EditRecipe.Neutral)], Ct);

        Assert.Empty(await edits.LoadAsync(Ct));
        await using var db = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(0, await db.PhotoEdits.CountAsync(Ct));
    }

    [Fact]
    public async Task InvalidRecipe_IsRefused_AndNothingInTheBatchIsSaved()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var a = (await AddPhotoAsync(services, "a.jpg")).Id;
        var b = (await AddPhotoAsync(services, "b.jpg")).Id;
        var edits = services.GetRequiredService<IEditService>();

        await Assert.ThrowsAsync<ArgumentException>(() => edits.SaveAsync([(a, Sample), (b, Sample with { Exposure = 9 })], Ct));

        Assert.Empty(await edits.LoadAsync(Ct));
    }

    [Fact]
    public async Task CorruptStoredRow_IsSkipped_NotFatal()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var good = (await AddPhotoAsync(services, "a.jpg")).Id;
        var bad = (await AddPhotoAsync(services, "b.jpg")).Id;
        await services.GetRequiredService<IEditService>().SaveAsync([(good, Sample)], Ct);
        await using (var db = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct))
        {
            db.PhotoEdits.Add(new PhotoEditEntity { PhotoId = bad, SchemaVersion = 1, RecipeJson = "{\"Exposure\": 42}", UpdatedUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(Ct);
        }

        var stored = await services.GetRequiredService<IEditRepository>().GetAllAsync(Ct);

        Assert.Equal(Sample, Assert.Single(stored.Recipes).Value);
        Assert.Equal(bad, Assert.Single(stored.Rejected).Id);
    }

    [Fact]
    public async Task DeletingAPhoto_DeletesItsEdit()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var id = (await AddPhotoAsync(services, "a.jpg")).Id;
        await services.GetRequiredService<IEditService>().SaveAsync([(id, Sample)], Ct);

        await using (var db = await services.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct))
        {
            await db.Photos.Where(p => p.Id == id).ExecuteDeleteAsync(Ct);
        }

        Assert.Empty(await services.GetRequiredService<IEditService>().LoadAsync(Ct));
    }

    [Fact]
    public async Task Presets_RoundTrip_Rename_AndDelete()
    {
        var preset = new EditPreset(Guid.NewGuid(), "Warm", Sample, PresetGroups.WhiteBalance | PresetGroups.Tone);
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            await first.GetRequiredService<IPresetRepository>().SaveAsync(preset, Ct);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);
        var presets = second.GetRequiredService<IPresetRepository>();
        Assert.Equal(preset, Assert.Single((await presets.GetAllAsync(Ct)).Presets));

        await presets.SaveAsync(preset with { Name = "Warm tone" }, Ct);
        Assert.Equal("Warm tone", Assert.Single((await presets.GetAllAsync(Ct)).Presets).Name);

        await presets.DeleteAsync(preset.Id, Ct);
        Assert.Empty((await presets.GetAllAsync(Ct)).Presets);
    }

    private static readonly EditRecipe WithMask = Sample with
    {
        LocalAdjustments = new(
        [
            new LocalAdjustment
            {
                Id = Guid.NewGuid(),
                Mask = new MaskReference { Kind = MaskKind.Subject, Source = "test-mask@1", Points = new([new MaskPoint(0.4, 0.6, true)]) },
                Exposure = 0.4,
                Highlights = -15,
            },
            new LocalAdjustment { Id = Guid.NewGuid(), Mask = new MaskReference { Kind = MaskKind.Sky, Source = "sky@1" }, Temperature = -10 },
        ]),
    };

    [Fact]
    public async Task RecipeWithMasks_RoundTripsExactly_AcrossRestart_AsVersion2()
    {
        Guid withMasks, without;
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            withMasks = (await AddPhotoAsync(first, "a.jpg")).Id;
            without = (await AddPhotoAsync(first, "b.jpg")).Id;
            await first.GetRequiredService<IEditService>().SaveAsync([(withMasks, WithMask), (without, Sample)], Ct);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);
        var loaded = await second.GetRequiredService<IEditService>().LoadAsync(Ct);
        Assert.Equal(WithMask, loaded[withMasks]);
        Assert.Equal(Sample, loaded[without]);

        // The version column records the stored form: 2 only where masks need it.
        await using var db = await second.GetRequiredService<IDbContextFactory<FrameForgeDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(2, (await db.PhotoEdits.SingleAsync(e => e.PhotoId == withMasks, Ct)).SchemaVersion);
        Assert.Equal(1, (await db.PhotoEdits.SingleAsync(e => e.PhotoId == without, Ct)).SchemaVersion);
    }

    [Fact]
    public async Task PresetWithMasks_RoundTrips()
    {
        var preset = new EditPreset(Guid.NewGuid(), "Lift subject", WithMask, PresetGroups.Exposure | PresetGroups.Masks);
        await using (var first = await TestServices.BuildMigratedAsync(_paths, Ct))
        {
            await first.GetRequiredService<IPresetRepository>().SaveAsync(preset, Ct);
        }

        await using var second = await TestServices.BuildMigratedAsync(_paths, Ct);

        Assert.Equal(preset, Assert.Single((await second.GetRequiredService<IPresetRepository>().GetAllAsync(Ct)).Presets));
    }

    [Fact]
    public async Task PresetNames_AreUnique_IgnoringCase()
    {
        await using var services = await TestServices.BuildMigratedAsync(_paths, Ct);
        var presets = services.GetRequiredService<IPresetRepository>();
        await presets.SaveAsync(new EditPreset(Guid.NewGuid(), "Warm", Sample, PresetGroups.All), Ct);

        await Assert.ThrowsAsync<DuplicatePresetNameException>(() =>
            presets.SaveAsync(new EditPreset(Guid.NewGuid(), "  WARM ", Sample, PresetGroups.All), Ct));
    }
}
