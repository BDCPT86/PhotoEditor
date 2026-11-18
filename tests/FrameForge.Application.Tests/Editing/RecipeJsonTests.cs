using FrameForge.Application.Editing;
using FrameForge.Core.Editing;

namespace FrameForge.Application.Tests.Editing;

/// <summary>Recipe schema v2 (Milestone 08 phase B, B2): the stored form, migration from v1, and what is refused.</summary>
public sealed class RecipeJsonTests
{
    /// <summary>Exactly what Milestone 06 wrote for these recipes (captured from that code before v2 was added).</summary>
    private const string V1Sample =
        """{"SchemaVersion":1,"Exposure":0.5,"Temperature":-12,"Tint":3,"Contrast":10,"Highlights":-20,"Shadows":15,"Saturation":5,"Vibrance":8,"QuarterTurns":1,"Straighten":1.5,"Crop":{"X":0.1,"Y":0.2,"Width":0.5,"Height":0.6,"IsFull":false},"HasGeometry":true,"HasTone":true,"HasColour":true,"IsNeutral":false}""";

    private const string V1Neutral =
        """{"SchemaVersion":1,"Exposure":0,"Temperature":0,"Tint":0,"Contrast":0,"Highlights":0,"Shadows":0,"Saturation":0,"Vibrance":0,"QuarterTurns":0,"Straighten":0,"Crop":{"X":0,"Y":0,"Width":1,"Height":1,"IsFull":true},"HasGeometry":false,"HasTone":false,"HasColour":false,"IsNeutral":true}""";

    private static readonly EditRecipe Sample = EditRecipe.Neutral with
    {
        Exposure = 0.5, Temperature = -12, Tint = 3, Contrast = 10, Highlights = -20, Shadows = 15, Saturation = 5, Vibrance = 8,
        QuarterTurns = 1, Straighten = 1.5, Crop = new CropRect(0.1, 0.2, 0.5, 0.6),
    };

    private static readonly Guid SubjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BackgroundId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static LocalAdjustment Subject => new()
    {
        Id = SubjectId,
        Mask = new MaskReference { Kind = MaskKind.Subject, Source = "yolox-s@2022nov+mobile-sam@f706ad9" },
        Exposure = 0.3,
        Shadows = 20,
    };

    private static LocalAdjustment Background => new()
    {
        Id = BackgroundId,
        Mask = new MaskReference
        {
            Kind = MaskKind.Subject,
            Source = "yolox-s@2022nov+mobile-sam@f706ad9",
            Invert = true,
            Points = new([new MaskPoint(0.25, 0.5, Include: true), new MaskPoint(0.9, 0.1, Include: false)]),
        },
        Saturation = -30,
        Exposure = -0.2,
    };

    private static EditRecipe WithMasks => Sample with { LocalAdjustments = new([Subject, Background]) };

    // ------------------------------------------------------------------ version 1 is unchanged

    [Fact]
    public void RecipeWithoutMasks_IsWrittenExactlyAsVersion1Was()
    {
        Assert.Equal(V1Sample, RecipeJson.Serialize(Sample));
        Assert.Equal(V1Neutral, RecipeJson.Serialize(EditRecipe.Neutral));
    }

    [Fact]
    public void StoredVersion1_LoadsUnchanged_AndIsCurrentInMemory()
    {
        var loaded = RecipeJson.TryDeserialize(V1Sample, out var error);

        Assert.Null(error);
        Assert.Equal(Sample, loaded);
        Assert.Equal(EditRecipe.CurrentSchemaVersion, loaded!.SchemaVersion);
        Assert.Empty(loaded.LocalAdjustments);
        Assert.Equal(1, loaded.RequiredSchemaVersion);
    }

    [Fact]
    public void Version1_RoundTripsByteForByte()
    {
        var loaded = RecipeJson.TryDeserialize(V1Sample, out _)!;

        Assert.Equal(V1Sample, RecipeJson.Serialize(loaded));
    }

    [Fact]
    public void RemovingTheLastMask_GoesBackToVersion1()
    {
        var withMask = Sample with { LocalAdjustments = new([Subject]) };

        Assert.Contains("\"SchemaVersion\":2", RecipeJson.Serialize(withMask), StringComparison.Ordinal);
        Assert.Equal(V1Sample, RecipeJson.Serialize(withMask with { LocalAdjustments = ValueList.Empty<LocalAdjustment>() }));
    }

    // ------------------------------------------------------------------ version 2

    [Fact]
    public void RecipeWithMasks_RoundTripsExactly()
    {
        var json = RecipeJson.Serialize(WithMasks);
        var loaded = RecipeJson.TryDeserialize(json, out var error);

        Assert.Null(error);
        Assert.Equal(WithMasks, loaded);
        Assert.Equal(json, RecipeJson.Serialize(loaded!));
    }

    [Fact]
    public void Version2_StoresTheMaskReference_NotTheMask()
    {
        var json = RecipeJson.Serialize(WithMasks);

        Assert.Contains("\"SchemaVersion\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"Kind\":\"Subject\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Invert\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"Points\":[{\"X\":0.25,\"Y\":0.5,\"Include\":true},{\"X\":0.9,\"Y\":0.1,\"Include\":false}]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputedProperties_AndEmptyLists_AreNotWritten()
    {
        var json = RecipeJson.Serialize(WithMasks);

        foreach (var name in new[] { "HasLocalAdjustments", "RequiredSchemaVersion", "HasNoEffect", "DisplayName" })
        {
            Assert.DoesNotContain("\"" + name + "\"", json, StringComparison.Ordinal);
        }

        // The subject mask has no clicks, so only the background's mask writes Points.
        Assert.Single(json.Split("\"Points\"")[1..]);
    }

    [Fact]
    public void MaskWithoutPointsField_ReadsAsNoPoints()
    {
        var json = RecipeJson.Serialize(Sample with { LocalAdjustments = new([Subject]) });

        var loaded = RecipeJson.TryDeserialize(json, out var error)!;

        Assert.Null(error);
        Assert.Empty(loaded.LocalAdjustments[0].Mask.Points);
    }

    // ------------------------------------------------------------------ refused

    [Fact]
    public void LaterVersion_IsRefused_NotHalfRead()
    {
        var json = V1Sample.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":3", StringComparison.Ordinal);

        Assert.Null(RecipeJson.TryDeserialize(json, out var error));
        Assert.Contains("Unsupported recipe version 3", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Version1WithMasks_IsRefused()
    {
        var json = RecipeJson.Serialize(WithMasks).Replace("\"SchemaVersion\":2", "\"SchemaVersion\":1", StringComparison.Ordinal);

        Assert.Null(RecipeJson.TryDeserialize(json, out var error));
        Assert.Contains("need recipe version 2", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"Kind\":\"Subject\"", "\"Kind\":\"Hair\"")]  // unknown kind
    [InlineData("\"Kind\":\"Subject\"", "\"Kind\":0")]         // kinds are names, not numbers
    [InlineData("\"Source\":\"[^\"]*\",", "")]                 // required field missing
    [InlineData("\"Id\":\"[^\"]*\",", "")]                     // required field missing
    public void BadMaskReference_IsRefused(string pattern, string replace)
    {
        var original = RecipeJson.Serialize(Sample with { LocalAdjustments = new([Subject]) });
        var json = System.Text.RegularExpressions.Regex.Replace(original, pattern, replace);
        Assert.NotEqual(original, json); // the case really changed the JSON

        Assert.Null(RecipeJson.TryDeserialize(json, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void OutOfRangeLocalSlider_IsRefused()
    {
        var json = RecipeJson.Serialize(Sample with { LocalAdjustments = new([Subject]) })
            .Replace("\"Shadows\":20", "\"Shadows\":250", StringComparison.Ordinal);

        Assert.Null(RecipeJson.TryDeserialize(json, out var error));
        Assert.Contains("Local adjustment 1: Shadows", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_RefusesAnInvalidRecipe() =>
        Assert.Throws<ArgumentException>(() => RecipeJson.Serialize(Sample with { LocalAdjustments = new([Subject, Subject]) }));
}
