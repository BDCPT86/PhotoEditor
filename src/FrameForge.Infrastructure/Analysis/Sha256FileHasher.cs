using System.Security.Cryptography;
using FrameForge.Application.Analysis;

namespace FrameForge.Infrastructure.Analysis;

/// <summary>Streams a file read-only through SHA-256. Cancellation is checked between 1 MB chunks.</summary>
public sealed class Sha256FileHasher : IFileHasher
{
    private const int BufferSize = 1024 * 1024;

    public string ComputeSha256(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
