using FrameForge.Application.Editing;
using FrameForge.Core.Decisions;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Editing;

public sealed class EditedExportOptionsTests
{
    private static readonly Photo Photo = new() { FilePath = @"C:\A\IMG_1.jpg", FileName = "IMG_1.jpg", FolderPath = @"C:\A" };
    private static readonly PhotoDecision Rated = new(CullState.Keep, 4, ColourLabel.Red);

    [Fact]
    public void Defaults_KeepAllMetadata_AndWriteTheRating()
    {
        var metadata = EditedExportOptions.Default.MetadataFor(Photo, Rated);

        Assert.Equal(Photo.FilePath, metadata.SourcePath);
        Assert.False(metadata.RemoveLocation);
        Assert.Equal(Rated, metadata.Decision);
    }

    [Fact]
    public void AllButLocation_AsksForLocationToBeRemoved()
    {
        var metadata = (EditedExportOptions.Default with { Metadata = ExportMetadata.AllButLocation }).MetadataFor(Photo, Rated);

        Assert.True(metadata.RemoveLocation);
        Assert.Equal(Photo.FilePath, metadata.SourcePath);
    }

    [Fact]
    public void None_WritesNothing_EvenWithTheRatingSwitchOn()
    {
        Assert.Same(JpegMetadata.Nothing, (EditedExportOptions.Default with { Metadata = ExportMetadata.None }).MetadataFor(Photo, Rated));
    }

    [Fact]
    public void RatingSwitchOff_LeavesTheOriginalsRatingAlone()
    {
        Assert.Null((EditedExportOptions.Default with { WriteRatingAndLabel = false }).MetadataFor(Photo, Rated).Decision);
    }

    [Fact]
    public void NoDecision_IsWrittenAsUnrated()
    {
        Assert.Equal(PhotoDecision.Unset, EditedExportOptions.Default.MetadataFor(Photo, null).Decision);
    }

    [Fact]
    public void UnknownMetadataChoice_IsInvalid()
    {
        Assert.NotEmpty((EditedExportOptions.Default with { Metadata = (ExportMetadata)42 }).Validate());
    }
}
