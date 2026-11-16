using System.Net;
using System.Security.Cryptography;
using FrameForge.Application.Ai;
using FrameForge.Infrastructure.Ai;
using FrameForge.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Infrastructure.Tests.Ai;

public sealed class FileSystemModelStoreTests : IDisposable
{
    private static readonly byte[] Content = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
    private readonly TempDirectory _temp = new();
    private readonly StubHandler _handler = new();
    private readonly HttpClient _http;
    private readonly FileSystemModelStore _store;

    public FileSystemModelStoreTests()
    {
        _http = new HttpClient(_handler);
        _store = new FileSystemModelStore(new AppDataPaths(_temp.Path), _http, NullLogger<FileSystemModelStore>.Instance);
    }

    private static ModelDescriptor Model(byte[]? content = null, string? sha = null)
    {
        var bytes = content ?? Content;
        return AiModelCatalog.FaceDetector with
        {
            Id = "test-model",
            Version = "v1",
            FileName = "model.onnx",
            SizeBytes = bytes.Length,
            Sha256 = sha ?? Convert.ToHexStringLower(SHA256.HashData(bytes)),
            DownloadUrl = new Uri("https://models.invalid/model.onnx"),
        };
    }

    private static ModelDownloadConsent Consent(ModelDescriptor m) => new(m.Id, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void NotInstalled_ByDefault()
    {
        Assert.Equal(ModelState.NotInstalled, _store.GetStatus(Model()).State);
    }

    [Fact]
    public async Task Download_VerifiesAndInstalls()
    {
        var model = Model();
        _handler.Body = Content;
        var reports = new List<double>();

        var path = await _store.DownloadAsync(model, Consent(model), new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

        Assert.Equal(Content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(ModelState.Installed, _store.GetStatus(model).State);
        Assert.Equal(model.DownloadUrl, _handler.LastRequest);
        Assert.Equal(1, reports[^1], 3);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!)); // no leftover .partial
    }

    [Fact]
    public async Task Download_WithChecksumMismatch_IsRejectedAndNothingInstalled()
    {
        var model = Model(sha: new string('0', 64));
        _handler.Body = Content;

        await Assert.ThrowsAsync<ModelVerificationException>(() =>
            _store.DownloadAsync(model, Consent(model), null, TestContext.Current.CancellationToken));

        Assert.Equal(ModelState.NotInstalled, _store.GetStatus(model).State);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_store.PathFor(model))!));
    }

    [Fact]
    public async Task Download_WithConsentForAnotherModel_IsRefusedBeforeAnyRequest()
    {
        var model = Model();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.DownloadAsync(model, new ModelDownloadConsent("other", DateTime.UtcNow), null, TestContext.Current.CancellationToken));
        Assert.Null(_handler.LastRequest);
    }

    [Fact]
    public async Task Download_HttpError_Throws_AndLeavesNothing()
    {
        var model = Model();
        _handler.Status = HttpStatusCode.NotFound;

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _store.DownloadAsync(model, Consent(model), null, TestContext.Current.CancellationToken));
        Assert.Equal(ModelState.NotInstalled, _store.GetStatus(model).State);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_store.PathFor(model))!));
    }

    [Fact]
    public async Task Import_VerifiesFile_ThenRemoveDeletesIt()
    {
        var model = Model();
        var source = Path.Combine(_temp.Path, "downloaded.onnx");
        await File.WriteAllBytesAsync(source, Content, TestContext.Current.CancellationToken);

        await _store.ImportAsync(model, source, TestContext.Current.CancellationToken);
        Assert.Equal(ModelState.Installed, _store.GetStatus(model).State);
        Assert.True(File.Exists(source)); // the user's file is copied, never moved

        _store.Remove(model);
        Assert.Equal(ModelState.NotInstalled, _store.GetStatus(model).State);
    }

    [Fact]
    public async Task Import_WrongFile_IsRejected()
    {
        var model = Model();
        var source = Path.Combine(_temp.Path, "wrong.onnx");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ModelVerificationException>(() => _store.ImportAsync(model, source, TestContext.Current.CancellationToken));
        Assert.Equal(ModelState.NotInstalled, _store.GetStatus(model).State);
    }

    [Fact]
    public async Task FileWithWrongSize_IsReportedInvalid()
    {
        var model = Model();
        var path = _store.PathFor(model);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);

        Assert.Equal(ModelState.Invalid, _store.GetStatus(model).State);
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        _temp.Dispose();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public byte[] Body { get; set; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public Uri? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
