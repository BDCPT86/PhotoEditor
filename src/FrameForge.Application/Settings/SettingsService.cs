using System.Text.Json;
using System.Text.Json.Serialization;
using FrameForge.Core.Settings;
using Microsoft.Extensions.Logging;

namespace FrameForge.Application.Settings;

public sealed partial class SettingsService(ISettingsStore store, ILogger<SettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<T> GetAsync<T>(SettingDefinition<T> setting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var json = await store.GetValueJsonAsync(setting.Key, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return setting.DefaultValue;
        }

        T? value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            LogUnreadableValue(ex, setting.Key);
            return setting.DefaultValue;
        }

        if (value is null || !setting.IsValid(value))
        {
            LogInvalidValue(setting.Key);
            return setting.DefaultValue;
        }

        return value;
    }

    public Task SetAsync<T>(SettingDefinition<T> setting, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (!setting.IsValid(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Value is not valid for setting '{setting.Key}'.");
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        return store.SetValueJsonAsync(setting.Key, json, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored value for setting {SettingKey} could not be read; using the default.")]
    private partial void LogUnreadableValue(Exception exception, string settingKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored value for setting {SettingKey} is not valid; using the default.")]
    private partial void LogInvalidValue(string settingKey);
}
