using System.Security.Cryptography;
using FrameForge.Application.Ai;
using Microsoft.Extensions.Logging;

namespace FrameForge.Infrastructure.Ai;

/// <summary>
/// Stores model files under <c>models/&lt;id&gt;/&lt;version&gt;/</c>. Every install (download or import) is
/// written to a temporary file, verified against the catalogue's SHA-256, then moved into place; a mismatch is
/// deleted and reported. Downloads go only to the catalogue's pinned URL and carry no user data.
/// </summary>
public sealed partial class FileSystemModelStore(AppDataPaths paths, HttpClient http, ILogger<FileSystemModelStore> logger) : IModelStore
{
    private const int BufferSize = 1024 * 1024;

    public ModelStatus GetStatus(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var path = PathFor(model);
        if (!File.Exists(path))
        {
            return new ModelStatus(model, ModelState.NotInstalled, null);
        }

        // Size is checked on every load (cheap); the full hash was verified at install time.
        return new FileInfo(path).Length == model.SizeBytes
            ? new ModelStatus(model, ModelState.Installed, path)
            : new ModelStatus(model, ModelState.Invalid, path);
    }

    public async Task<string> DownloadAsync(ModelDescriptor model, ModelDownloadConsent consent, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(consent);
        if (!string.Equals(consent.ModelId, model.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("Consent was given for a different model.", nameof(consent));
        }

        LogDownloadStarting(model.VersionTag, model.DownloadUrl, consent.AcceptedUtc);
        var temp = TempPathFor(model);
        try
        {
            using (var response = await http.GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using (input.ConfigureAwait(false))
                {
                    await CopyWithProgressAsync(input, temp, model.SizeBytes, progress, cancellationToken).ConfigureAwait(false);
                }
            }

            return Install(model, temp);
        }
        finally
        {
            DeleteIfExists(temp);
        }
    }

    public async Task<string> ImportAsync(ModelDescriptor model, string sourceFile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        var temp = TempPathFor(model);
        try
        {
            var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (input.ConfigureAwait(false))
            {
                await CopyWithProgressAsync(input, temp, model.SizeBytes, null, cancellationToken).ConfigureAwait(false);
            }

            LogImported(model.VersionTag);
            return Install(model, temp);
        }
        finally
        {
            DeleteIfExists(temp);
        }
    }

    public void Remove(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var directory = Path.GetDirectoryName(PathFor(model))!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
            LogRemoved(model.VersionTag);
        }
    }

    internal string PathFor(ModelDescriptor model) => Path.Combine(paths.ModelsDirectory, model.Id, model.Version, model.FileName);

    private string TempPathFor(ModelDescriptor model)
    {
        var directory = Path.GetDirectoryName(PathFor(model))!;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Guid.NewGuid():N}.partial");
    }

    private string Install(ModelDescriptor model, string temp)
    {
        var size = new FileInfo(temp).Length;
        string hash;
        using (var stream = File.OpenRead(temp))
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        if (size != model.SizeBytes || !string.Equals(hash, model.Sha256, StringComparison.Ordinal))
        {
            LogVerificationFailed(model.VersionTag, size, hash);
            throw new ModelVerificationException(
                $"The file is not the expected {model.Name}: expected {model.SizeBytes:N0} bytes with SHA-256 {model.Sha256}, got {size:N0} bytes with {hash}. It was not installed.");
        }

        var target = PathFor(model);
        File.Move(temp, target, overwrite: true);
        LogInstalled(model.VersionTag, target);
        return target;
    }

    private static async Task CopyWithProgressAsync(Stream input, string targetPath, long expected, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        await using (output.ConfigureAwait(false))
        {
            var buffer = new byte[BufferSize];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                if (total > expected * 2)
                {
                    throw new ModelVerificationException("The download is much larger than the catalogued model; stopped.");
                }

                progress?.Report(expected == 0 ? 0 : Math.Min(1, (double)total / expected));
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Model download started (user consent at {ConsentUtc:o}): {Model} from {Url}")]
    private partial void LogDownloadStarting(string model, Uri url, DateTime consentUtc);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model import started: {Model}")]
    private partial void LogImported(string model);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model installed and verified: {Model} at {Path}")]
    private partial void LogInstalled(string model, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model verification failed for {Model}: {Size} bytes, sha256 {Hash}; not installed")]
    private partial void LogVerificationFailed(string model, long size, string hash);

    [LoggerMessage(Level = LogLevel.Information, Message = "Model removed: {Model}")]
    private partial void LogRemoved(string model);
}
