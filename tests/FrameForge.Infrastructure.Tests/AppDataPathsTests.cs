namespace FrameForge.Infrastructure.Tests;

// Changes a process-wide environment variable, so it must not run alongside other tests that read it.
[Collection(nameof(SerialEnvironmentTests))]
public sealed class AppDataPathsTests
{
    [Fact]
    public void Override_RedirectsEveryAppDataLocation_AndDefaultIsLocalAppData()
    {
        var previous = Environment.GetEnvironmentVariable(AppDataPaths.DataDirectoryOverrideVariable);
        var testRoot = Path.Combine(Path.GetTempPath(), "FrameForge.Override.Test");
        try
        {
            Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryOverrideVariable, testRoot);
            var overridden = AppDataPaths.ForCurrentUser();
            Assert.True(AppDataPaths.IsOverridden);
            Assert.Equal(testRoot, overridden.RootDirectory);
            Assert.StartsWith(testRoot, overridden.DatabasePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(testRoot, overridden.LogsDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(testRoot, overridden.ThumbnailsDirectory, StringComparison.OrdinalIgnoreCase);

            Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryOverrideVariable, null);
            var normal = AppDataPaths.ForCurrentUser();
            Assert.False(AppDataPaths.IsOverridden);
            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge"),
                normal.RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryOverrideVariable, previous);
        }
    }
}

[CollectionDefinition(nameof(SerialEnvironmentTests), DisableParallelization = true)]
public sealed class SerialEnvironmentTests;
