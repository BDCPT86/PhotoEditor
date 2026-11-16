using FrameForge.Application.Settings;

namespace FrameForge.Application.Tests.Settings;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetValueJsonAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out var json) ? json : null);

    public Task SetValueJsonAsync(string key, string valueJson, CancellationToken cancellationToken = default)
    {
        Values[key] = valueJson;
        return Task.CompletedTask;
    }
}
