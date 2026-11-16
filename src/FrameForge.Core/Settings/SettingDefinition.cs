namespace FrameForge.Core.Settings;

/// <summary>
/// Describes a single persisted application setting: its storage key, default value,
/// and the rule a stored or user-supplied value must satisfy.
/// </summary>
public sealed class SettingDefinition<T>
{
    private readonly Func<T, bool>? _validator;

    public SettingDefinition(string key, T defaultValue, Func<T, bool>? validator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        if (validator is not null && !validator(defaultValue))
        {
            throw new ArgumentException($"Default value for setting '{key}' does not satisfy its own validation rule.", nameof(defaultValue));
        }

        Key = key;
        DefaultValue = defaultValue;
        _validator = validator;
    }

    public string Key { get; }

    public T DefaultValue { get; }

    public bool IsValid(T value) => value is not null && (_validator?.Invoke(value) ?? true);
}
