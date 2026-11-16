using FrameForge.Core.Settings;

namespace FrameForge.Application.Settings;

public interface ISettingsService
{
    /// <summary>
    /// Reads a setting. Returns the setting's default when nothing is stored or when the
    /// stored value cannot be read or is no longer valid (the latter is logged).
    /// </summary>
    Task<T> GetAsync<T>(SettingDefinition<T> setting, CancellationToken cancellationToken = default);

    /// <summary>Validates and persists a setting value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value fails the setting's validation rule.</exception>
    Task SetAsync<T>(SettingDefinition<T> setting, T value, CancellationToken cancellationToken = default);
}
