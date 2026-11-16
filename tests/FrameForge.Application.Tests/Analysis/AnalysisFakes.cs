using FrameForge.Analysis;
using FrameForge.Application.Analysis;
using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Analysis;

internal sealed class MemoryAnalysisRepository : IAnalysisResultRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid, AnalysisKind), StoredAnalysis> _rows = [];

    public IReadOnlyList<StoredAnalysis> Rows
    {
        get
        {
            lock (_gate)
            {
                return _rows.Values.ToList();
            }
        }
    }

    public Task<IReadOnlyList<StoredAnalysis>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows);

    public Task SaveAsync(IReadOnlyCollection<StoredAnalysis> results, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var r in results)
            {
                _rows[(r.PhotoId, r.Kind)] = r;
            }
        }

        return Task.CompletedTask;
    }

    public void Replace(StoredAnalysis row)
    {
        lock (_gate)
        {
            _rows[(row.PhotoId, row.Kind)] = row;
        }
    }
}

/// <summary>Returns a distinct synthetic image per file name; can fail or block for chosen paths; tracks concurrency.</summary>
internal sealed class FakeImageLoader : IAnalysisImageLoader
{
    private int _active;
    private int _loads;

    public HashSet<string> Corrupt { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Action<int>? AfterLoad { get; set; }

    public TimeSpan Delay { get; set; }

    public int Loads => Volatile.Read(ref _loads);

    public int MaxConcurrent { get; private set; }

    public AnalysisImage Load(string path, int maxPixelSize)
    {
        var active = Interlocked.Increment(ref _active);
        lock (this)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, active);
        }

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                Thread.Sleep(Delay);
            }

            if (Corrupt.Contains(path))
            {
                throw new PhotoReadException("corrupt (test)");
            }

            var seed = path.GetHashCode(StringComparison.Ordinal);
            return AnalysisImage.Create(64, 48, (x, y) => ((byte)((x * 4) + seed), (byte)((y * 5) + (seed >> 8)), (byte)((x + y) * 2)));
        }
        finally
        {
            Interlocked.Decrement(ref _active);
            var count = Interlocked.Increment(ref _loads);
            AfterLoad?.Invoke(count);
        }
    }
}

internal sealed class FakeHasher : IFileHasher
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public string ComputeSha256(string path, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return "hash-of-" + Path.GetFileName(path).ToLowerInvariant();
    }
}

internal static class AnalysisTestPhotos
{
    public static Photo Create(string name, ImportStatus status = ImportStatus.Imported, long size = 1000) => new()
    {
        Id = Guid.NewGuid(),
        FilePath = $@"C:\Shoot\{name}",
        FileName = name,
        FolderPath = @"C:\Shoot",
        FileSizeBytes = size,
        LastModifiedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Status = status,
    };
}
