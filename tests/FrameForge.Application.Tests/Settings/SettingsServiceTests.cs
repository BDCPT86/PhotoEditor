using FrameForge.Application.Settings;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Tests.Settings;

public sealed class SettingsServiceTests
{
    private readonly InMemorySettingsStore _store = new();
    private readonly RecordingLogger<SettingsService> _logger = new();
    private readonly SettingsService _service;

    public SettingsServiceTests() => _service = new SettingsService(_store, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetAsync_ReturnsDefaults_WhenNothingIsStored()
    {
        Assert.Equal(AppPage.Library, await _service.GetAsync(AppSettings.StartPage, Ct));
        Assert.Equal(AppSettings.DefaultLogRetentionDays, await _service.GetAsync(AppSettings.LogRetentionDays, Ct));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsValues()
    {
        await _service.SetAsync(AppSettings.StartPage, AppPage.About, Ct);
        await _service.SetAsync(AppSettings.LogRetentionDays, 30, Ct);

        Assert.Equal(AppPage.About, await _service.GetAsync(AppSettings.StartPage, Ct));
        Assert.Equal(30, await _service.GetAsync(AppSettings.LogRetentionDays, Ct));
    }

    [Fact]
    public async Task SetAsync_StoresEnumsByName_SoReorderingTheEnumDoesNotCorruptSettings()
    {
        await _service.SetAsync(AppSettings.StartPage, AppPage.Settings, Ct);

        Assert.Equal("\"Settings\"", _store.Values[AppSettings.StartPage.Key]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(AppSettings.MaxLogRetentionDays + 1)]
    public async Task SetAsync_RejectsInvalidValues_AndDoesNotPersistThem(int days)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.SetAsync(AppSettings.LogRetentionDays, days, Ct));

        Assert.False(_store.Values.ContainsKey(AppSettings.LogRetentionDays.Key));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("\"twelve\"")]
    public async Task GetAsync_ReturnsDefaultAndLogsWarning_WhenStoredValueIsUnreadable(string storedJson)
    {
        _store.Values[AppSettings.LogRetentionDays.Key] = storedJson;

        var days = await _service.GetAsync(AppSettings.LogRetentionDays, Ct);

        Assert.Equal(AppSettings.DefaultLogRetentionDays, days);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(AppSettings.LogRetentionDays.Key, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100000")]
    public async Task GetAsync_ReturnsDefaultAndLogsWarning_WhenStoredValueFailsValidation(string storedJson)
    {
        _store.Values[AppSettings.LogRetentionDays.Key] = storedJson;

        var days = await _service.GetAsync(AppSettings.LogRetentionDays, Ct);

        Assert.Equal(AppSettings.DefaultLogRetentionDays, days);
        Assert.Equal(LogLevel.Warning, Assert.Single(_logger.Entries).Level);
    }

    [Theory]
    [InlineData("\"NoSuchPage\"")]
    [InlineData("42")]
    public async Task GetAsync_ReturnsDefaultStartPage_WhenStoredEnumIsUnknown(string storedJson)
    {
        _store.Values[AppSettings.StartPage.Key] = storedJson;

        Assert.Equal(AppSettings.StartPage.DefaultValue, await _service.GetAsync(AppSettings.StartPage, Ct));
        Assert.Single(_logger.Entries);
    }
}
