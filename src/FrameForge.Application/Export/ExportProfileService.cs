using FrameForge.Application.Settings;
using FrameForge.Core.Settings;

namespace FrameForge.Application.Export;

/// <summary>Saved export profiles and the last export's settings, kept in the settings store.</summary>
public interface IExportProfileService
{
    /// <summary>The stored set; empty when nothing is stored or it can't be read (the settings service logs that).</summary>
    Task<ExportProfileSet> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces a user profile and returns the updated set.</summary>
    /// <exception cref="ArgumentException">The profile is not valid or uses a built-in name.</exception>
    Task<ExportProfileSet> SaveProfileAsync(ExportProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Removes a user profile and returns the updated set.</summary>
    Task<ExportProfileSet> DeleteProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Remembers the settings of an export that is starting.</summary>
    Task SaveLastUsedAsync(ExportProfile lastUsed, CancellationToken cancellationToken = default);
}

public sealed class ExportProfileService(ISettingsService settings) : IExportProfileService
{
    /// <summary>One JSON document, so profiles and the last-used settings change together.</summary>
    public static readonly SettingDefinition<ExportProfileSet> Setting = new("export.profiles", ExportProfileSet.Empty, set => set.IsValid());

    public Task<ExportProfileSet> LoadAsync(CancellationToken cancellationToken = default) => settings.GetAsync(Setting, cancellationToken);

    public async Task<ExportProfileSet> SaveProfileAsync(ExportProfile profile, CancellationToken cancellationToken = default)
    {
        var updated = ExportProfiles.Save(await LoadAsync(cancellationToken).ConfigureAwait(false), profile);
        await settings.SetAsync(Setting, updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<ExportProfileSet> DeleteProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var updated = ExportProfiles.Delete(await LoadAsync(cancellationToken).ConfigureAwait(false), name);
        await settings.SetAsync(Setting, updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task SaveLastUsedAsync(ExportProfile lastUsed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lastUsed);
        if (lastUsed.Validate().Count > 0)
        {
            throw new ArgumentException("The export settings are not valid.", nameof(lastUsed));
        }

        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        await settings.SetAsync(Setting, current with { LastUsed = lastUsed }, cancellationToken).ConfigureAwait(false);
    }
}
