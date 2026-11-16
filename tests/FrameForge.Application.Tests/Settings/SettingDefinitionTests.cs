using FrameForge.Core.Settings;

namespace FrameForge.Application.Tests.Settings;

public sealed class SettingDefinitionTests
{
    [Fact]
    public void Constructor_RejectsDefaultThatFailsItsOwnValidation() =>
        Assert.Throws<ArgumentException>(() => new SettingDefinition<int>("test.key", 0, v => v > 0));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankKey(string key) =>
        Assert.ThrowsAny<ArgumentException>(() => new SettingDefinition<int>(key, 1));

    [Fact]
    public void AppSettingKeys_AreUnique()
    {
        string[] keys =
        [
            AppSettings.StartPage.Key,
            AppSettings.LogRetentionDays.Key,
            AppSettings.ImportIncludeSubfolders.Key,
            AppSettings.ImportLastFolder.Key,
        ];
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
