using FrameForge.Core.Editing;

namespace FrameForge.Rendering.Tests;

public sealed class RecipeTests
{
    [Fact]
    public void Neutral_IsValid_AndNeutral()
    {
        Assert.Empty(EditRecipe.Neutral.Validate());
        Assert.True(EditRecipe.Neutral.IsNeutral);
    }

    [Theory]
    [InlineData(nameof(EditRecipe.Exposure), 5.01)]
    [InlineData(nameof(EditRecipe.Contrast), -100.5)]
    [InlineData(nameof(EditRecipe.Saturation), double.NaN)]
    [InlineData(nameof(EditRecipe.Straighten), 46)]
    [InlineData(nameof(EditRecipe.Shadows), double.PositiveInfinity)]
    public void OutOfRangeValues_AreRejected(string field, double value)
    {
        var recipe = field switch
        {
            nameof(EditRecipe.Exposure) => EditRecipe.Neutral with { Exposure = value },
            nameof(EditRecipe.Contrast) => EditRecipe.Neutral with { Contrast = value },
            nameof(EditRecipe.Saturation) => EditRecipe.Neutral with { Saturation = value },
            nameof(EditRecipe.Straighten) => EditRecipe.Neutral with { Straighten = value },
            _ => EditRecipe.Neutral with { Shadows = value },
        };

        var error = Assert.Single(recipe.Validate());
        Assert.StartsWith(field, error, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => recipe.EnsureValid());
    }

    [Theory]
    [InlineData(0.5, 0, 0.6, 1)]       // runs past the right edge
    [InlineData(-0.1, 0, 0.5, 0.5)]    // negative origin
    [InlineData(0, 0, 0.01, 0.5)]      // too thin
    public void BadCrops_AreRejected(double x, double y, double w, double h)
    {
        Assert.NotEmpty((EditRecipe.Neutral with { Crop = new CropRect(x, y, w, h) }).Validate());
    }

    [Fact]
    public void BadQuarterTurns_AndFutureVersions_AreRejected()
    {
        Assert.NotEmpty((EditRecipe.Neutral with { QuarterTurns = 4 }).Validate());
        Assert.NotEmpty((EditRecipe.Neutral with { SchemaVersion = EditRecipe.CurrentSchemaVersion + 1 }).Validate());
    }

    [Fact]
    public void Preset_ChangesOnlyItsGroups()
    {
        var preset = new EditPreset(Guid.NewGuid(), "Warm pop", EditRecipe.Neutral with { Temperature = 30, Saturation = 25, Exposure = 2, Crop = new CropRect(0, 0, 0.5, 0.5) }, PresetGroups.WhiteBalance | PresetGroups.Colour);
        var target = EditRecipe.Neutral with { Exposure = -0.5, Straighten = 3 };

        var result = preset.ApplyTo(target);

        Assert.Equal(30, result.Temperature);
        Assert.Equal(25, result.Saturation);
        Assert.Equal(-0.5, result.Exposure);          // not in the preset's groups
        Assert.Equal(3, result.Straighten);
        Assert.True(result.Crop.IsFull);
    }

    // ------------------------------------------------------------------ local adjustments (schema v2)

    private static LocalAdjustment Local(MaskKind kind = MaskKind.Subject, bool invert = false, double exposure = 0, IEnumerable<MaskPoint>? points = null) => new()
    {
        Id = Guid.NewGuid(),
        Mask = new MaskReference { Kind = kind, Source = "test-mask@1", Invert = invert, Points = new(points ?? []) },
        Exposure = exposure,
    };

    [Fact]
    public void RecipesWithTheSameMasks_AreEqual()
    {
        var a = Local(exposure: 0.5);
        var first = EditRecipe.Neutral with { LocalAdjustments = new([a]) };
        var second = EditRecipe.Neutral with { LocalAdjustments = new([a with { }]) };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { LocalAdjustments = new([a with { Exposure = 0.6 }]) });
    }

    [Fact]
    public void AMaskWithNoEffect_StillMakesTheRecipeNonNeutral()
    {
        var recipe = EditRecipe.Neutral with { LocalAdjustments = new([Local()]) };

        Assert.True(recipe.LocalAdjustments[0].HasNoEffect);
        Assert.False(recipe.IsNeutral); // kept, so the user's mask is not thrown away before they move a slider
        Assert.Equal(2, recipe.RequiredSchemaVersion);
        Assert.Equal(1, EditRecipe.Neutral.RequiredSchemaVersion);
    }

    [Fact]
    public void LocalAdjustments_AreValidated()
    {
        Assert.Empty((EditRecipe.Neutral with { LocalAdjustments = new([Local(), Local(MaskKind.Sky)]) }).Validate());

        Assert.Contains("Exposure", Assert.Single((EditRecipe.Neutral with { LocalAdjustments = new([Local(exposure: 6)]) }).Validate()), StringComparison.Ordinal);
        Assert.NotEmpty((EditRecipe.Neutral with { LocalAdjustments = new([Local((MaskKind)7)]) }).Validate());
        Assert.NotEmpty((EditRecipe.Neutral with { LocalAdjustments = new([Local() with { Id = Guid.Empty }]) }).Validate());
        Assert.NotEmpty((EditRecipe.Neutral with { LocalAdjustments = new([Local(points: [new MaskPoint(1.2, 0.5, true)])]) }).Validate());
        Assert.NotEmpty((EditRecipe.Neutral with { LocalAdjustments = new([Local() with { Mask = Local().Mask with { Source = " " } }]) }).Validate());
        var twice = Local();
        Assert.Contains("unique", Assert.Single((EditRecipe.Neutral with { LocalAdjustments = new([twice, twice]) }).Validate()), StringComparison.Ordinal);
        Assert.NotEmpty((EditRecipe.Neutral with { LocalAdjustments = new(Enumerable.Range(0, EditRecipe.MaxLocalAdjustments + 1).Select(_ => Local())) }).Validate());
    }

    [Fact]
    public void Version1_CannotHoldMasks()
    {
        var recipe = EditRecipe.Neutral with { SchemaVersion = 1, LocalAdjustments = new([Local()]) };

        Assert.Contains("need recipe version 2", Assert.Single(recipe.Validate()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MaskKind.Subject, false, "Subject")]
    [InlineData(MaskKind.Subject, true, "Background")]
    [InlineData(MaskKind.Sky, false, "Sky")]
    public void MaskNames_ForTheUser(MaskKind kind, bool invert, string name) =>
        Assert.Equal(name, Local(kind, invert).Mask.DisplayName);

    [Fact]
    public void PresetWithMasks_ReplacesThePhotosMasks_WithoutItsClicks()
    {
        var source = Local(exposure: 0.4, points: [new MaskPoint(0.5, 0.5, true)]);
        var preset = new EditPreset(Guid.NewGuid(), "Lift subject", EditRecipe.Neutral with { LocalAdjustments = new([source]) }, PresetGroups.Masks);
        var target = EditRecipe.Neutral with { Exposure = 1, LocalAdjustments = new([Local(MaskKind.Sky, exposure: -1)]) };

        var result = preset.ApplyTo(target);

        var applied = Assert.Single(result.LocalAdjustments);
        Assert.Equal(0.4, applied.Exposure);
        Assert.Equal(MaskKind.Subject, applied.Mask.Kind);
        Assert.Empty(applied.Mask.Points); // clicks belong to the photo they were made on
        Assert.Equal(1, result.Exposure);  // not in the preset's groups
    }

    [Fact]
    public void AllGroupsPreset_LeavesThePhotosMasksAlone()
    {
        // "All" is stored by name and meant the Milestone 06 groups; it must not start replacing masks.
        var preset = new EditPreset(Guid.NewGuid(), "Old preset", EditRecipe.Neutral with { Exposure = 0.3 }, PresetGroups.All);
        var mask = Local(exposure: 0.5);

        var result = preset.ApplyTo(EditRecipe.Neutral with { LocalAdjustments = new([mask]) });

        Assert.Equal(mask, Assert.Single(result.LocalAdjustments));
        Assert.False(PresetGroups.All.HasFlag(PresetGroups.Masks));
        Assert.Empty(new EditPreset(Guid.NewGuid(), "Masks only", EditRecipe.Neutral, PresetGroups.Masks).Validate());
    }

    [Theory]
    [InlineData("", PresetGroups.All)]
    [InlineData("ok", PresetGroups.None)]
    [InlineData("ok", (PresetGroups)64)]
    public void InvalidPresets_AreRejected(string name, PresetGroups groups)
    {
        Assert.NotEmpty(new EditPreset(Guid.NewGuid(), name, EditRecipe.Neutral, groups).Validate());
    }
}
