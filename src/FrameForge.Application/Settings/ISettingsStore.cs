namespace FrameForge.Application.Settings;

/// <summary>
/// Persistence port for raw setting values. Values are opaque JSON strings;
/// typing, defaults, and validation belong to <see cref="ISettingsService"/>.
/// </summary>
public interface ISettingsStore
{
    /// <returns>The stored JSON, or <see langword="null"/> if the key has never been saved.</returns>
    Task<string?> GetValueJsonAsync(string key, CancellationToken cancellationToken = default);

    Task SetValueJsonAsync(string key, string valueJson, CancellationToken cancellationToken = default);
}
