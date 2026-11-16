using System.Text.Json;
using FrameForge.Core.Editing;

namespace FrameForge.Application.Editing;

/// <summary>Recipes as stored, plus rows that could not be read (reported, never silently dropped).</summary>
public sealed record StoredEdits(IReadOnlyDictionary<Guid, EditRecipe> Recipes, IReadOnlyList<(Guid Id, string Reason)> Rejected);

public sealed record StoredPresets(IReadOnlyList<EditPreset> Presets, IReadOnlyList<(Guid Id, string Reason)> Rejected);

public interface IEditRepository
{
    Task<StoredEdits> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves recipes in one transaction; a neutral recipe deletes the photo's row.</summary>
    Task SaveAsync(IReadOnlyCollection<(Guid PhotoId, EditRecipe Recipe)> recipes, CancellationToken cancellationToken = default);
}

public interface IPresetRepository
{
    Task<StoredPresets> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces by id. Names are unique (case-insensitive).</summary>
    /// <exception cref="DuplicatePresetNameException">Another preset already has this name.</exception>
    Task SaveAsync(EditPreset preset, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class DuplicatePresetNameException : Exception
{
    public DuplicatePresetNameException()
    {
    }

    public DuplicatePresetNameException(string message)
        : base(message)
    {
    }

    public DuplicatePresetNameException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Stable JSON form of a recipe. Unknown fields are ignored; the result is always validated.</summary>
public static class RecipeJson
{
    private static readonly JsonSerializerOptions Options = new() { AllowTrailingCommas = true };

    public static string Serialize(EditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return JsonSerializer.Serialize(recipe.EnsureValid(), Options);
    }

    /// <summary>Parses and validates; returns null and a reason when the stored text is not a usable recipe.</summary>
    public static EditRecipe? TryDeserialize(string json, out string? error)
    {
        try
        {
            var recipe = JsonSerializer.Deserialize<EditRecipe>(json, Options);
            if (recipe is null)
            {
                error = "Empty recipe.";
                return null;
            }

            var problems = recipe.Validate();
            error = problems.Count == 0 ? null : string.Join(" ", problems);
            return problems.Count == 0 ? recipe : null;
        }
        catch (JsonException ex)
        {
            error = "Not a readable recipe: " + ex.Message;
            return null;
        }
    }
}
