using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Photos;

public sealed class CameraNameTests
{
    [Theory]
    [InlineData("Canon", "Canon EOS R50", "Canon EOS R50")]
    [InlineData("NIKON CORPORATION", "NIKON Z 6", "NIKON CORPORATION NIKON Z 6")]
    [InlineData("SONY", "ILCE-7M3", "SONY ILCE-7M3")]
    [InlineData("canon", "Canon EOS R5", "Canon EOS R5")]
    [InlineData(" Canon ", " Canon EOS R50 ", "Canon EOS R50")]
    [InlineData(null, "Model X", "Model X")]
    [InlineData("TestMake", null, "TestMake")]
    [InlineData("TestMake", "  ", "TestMake")]
    [InlineData(null, null, "")]
    public void Format_JoinsMakeAndModel_WithoutRepeatingTheMake(string? make, string? model, string expected)
        => Assert.Equal(expected, CameraName.Format(make, model));
}
