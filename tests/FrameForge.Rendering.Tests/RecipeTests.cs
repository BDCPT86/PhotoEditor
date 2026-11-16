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

    [Theory]
    [InlineData("", PresetGroups.All)]
    [InlineData("ok", PresetGroups.None)]
    [InlineData("ok", (PresetGroups)64)]
    public void InvalidPresets_AreRejected(string name, PresetGroups groups)
    {
        Assert.NotEmpty(new EditPreset(Guid.NewGuid(), name, EditRecipe.Neutral, groups).Validate());
    }
}
