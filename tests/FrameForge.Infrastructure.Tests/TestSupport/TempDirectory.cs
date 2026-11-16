using Microsoft.Data.Sqlite;

namespace FrameForge.Infrastructure.Tests.TestSupport;

/// <summary>A unique, self-deleting directory under the system temp folder.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FrameForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        // Pooled SQLite connections keep the database file open on Windows.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail an otherwise passing test.
        }
    }
}
