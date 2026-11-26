using FrameForge.Application.Photos;
using FrameForge.Core.Photos;

namespace FrameForge.Application.Tests.Photos;

/// <summary>In-memory file system: a set of files, plus folders that "cannot be read".</summary>
internal sealed class FakeEnumerator : IPhotoFileEnumerator
{
    private readonly Dictionary<string, PhotoFileInfo> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> UnreadableFolders { get; } = [];

    /// <summary>What the scan reports as photos in formats that aren't imported, by extension.</summary>
    public Dictionary<string, int> UnsupportedFiles { get; } = new(StringComparer.Ordinal);

    public void AddFile(string path, long size = 1000, DateTime? modifiedUtc = null) =>
        _files[path] = new PhotoFileInfo(path, size, modifiedUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    public void RemoveFile(string path) => _files.Remove(path);

    public PhotoFolderScan Scan(string folderPath, bool includeSubfolders, CancellationToken cancellationToken)
    {
        var files = _files.Values
            .Where(f =>
            {
                var folder = Path.GetDirectoryName(f.FilePath)!;
                if (UnreadableFolders.Any(u => FolderScope.IsSameOrUnder(folder, u)))
                {
                    return false;
                }

                return includeSubfolders
                    ? FolderScope.IsSameOrUnder(folder, folderPath)
                    : string.Equals(folder, folderPath, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PhotoFolderScan(files, UnreadableFolders.ToList()) { UnsupportedFiles = new Dictionary<string, int>(UnsupportedFiles) };
    }
}

internal sealed class FakeMetadataReader : IPhotoMetadataReader
{
    private int _readCount;

    public HashSet<string> CorruptFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Invoked after each read with the running count; lets a test cancel mid-import.</summary>
    public Action<int>? AfterRead { get; set; }

    public int ReadCount => Volatile.Read(ref _readCount);

    public PhotoMetadata Read(string filePath)
    {
        var count = Interlocked.Increment(ref _readCount);
        try
        {
            if (CorruptFiles.Contains(filePath))
            {
                throw new PhotoReadException("corrupt test file");
            }

            return new PhotoMetadata { Width = 6000, Height = 4000, CameraModel = "Test Camera" };
        }
        finally
        {
            AfterRead?.Invoke(count);
        }
    }
}

internal sealed class InMemoryPhotoRepository : IPhotoRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Photo> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public int UpsertCalls { get; private set; }

    public IReadOnlyList<Photo> All
    {
        get
        {
            lock (_gate)
            {
                return _byPath.Values.ToList();
            }
        }
    }

    public Photo Get(string path)
    {
        lock (_gate)
        {
            return _byPath[path];
        }
    }

    public Task<IReadOnlyList<PhotoFileState>> GetFileStatesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<PhotoFileState> states = _byPath.Values
                .Select(p => new PhotoFileState(p.Id, p.FilePath, p.FolderPath, p.FileSizeBytes, p.LastModifiedUtc, p.Status))
                .ToList();
            return Task.FromResult(states);
        }
    }

    public Task UpsertAsync(IReadOnlyCollection<Photo> photos, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            UpsertCalls++;
            foreach (var photo in photos)
            {
                if (_byPath.TryGetValue(photo.FilePath, out var existing))
                {
                    photo.Id = existing.Id;
                    photo.ImportedUtc = existing.ImportedUtc;
                }

                _byPath[photo.FilePath] = photo;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkMissingAsync(IReadOnlyCollection<Guid> photoIds, string reason, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var photo in _byPath.Values.Where(p => photoIds.Contains(p.Id)))
            {
                photo.Status = ImportStatus.Missing;
                photo.LastError = reason;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Photo>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Photo>>(All.OrderBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase).ToList());

    public Task<int> RelocateAsync(IReadOnlyCollection<PhotoMove> moves, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var moved = 0;
            foreach (var move in moves)
            {
                var photo = _byPath.Values.Single(p => p.Id == move.PhotoId);
                _byPath.Remove(photo.FilePath);
                photo.FilePath = move.NewFilePath;
                photo.FileName = Path.GetFileName(move.NewFilePath);
                photo.FolderPath = Path.GetDirectoryName(move.NewFilePath)!;
                if (photo.Status == ImportStatus.Missing)
                {
                    photo.Status = ImportStatus.Imported;
                    photo.LastError = null;
                }

                _byPath[photo.FilePath] = photo;
                moved++;
            }

            return Task.FromResult(moved);
        }
    }
}

/// <summary>Synchronous IProgress (Progress&lt;T&gt; posts asynchronously, which makes tests racy).</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _reports = [];

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_gate)
            {
                return _reports.ToList();
            }
        }
    }

    public void Report(T value)
    {
        lock (_gate)
        {
            _reports.Add(value);
        }
    }
}
