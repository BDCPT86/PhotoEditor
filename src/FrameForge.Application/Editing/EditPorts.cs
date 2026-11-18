using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

/// <summary>
/// Stable JSON form of a recipe. Unknown fields are ignored; the result is always validated.
/// <list type="bullet">
/// <item><b>Writing:</b> a recipe is written as the oldest version that can hold it
/// (<see cref="EditRecipe.RequiredSchemaVersion"/>). Without local adjustments that is version 1, byte for byte the
/// form Milestone 06 wrote, so earlier versions of the app can still read it.</item>
/// <item><b>Reading:</b> versions 1 and 2 are accepted and upgraded to <see cref="EditRecipe.CurrentSchemaVersion"/> in
/// memory (a version 1 recipe simply has no local adjustments). Later versions are refused, never half-read.</item>
/// </list>
/// </summary>
public static class RecipeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        Converters =
        {
            new ValueListConverter<LocalAdjustment>(),
            new ValueListConverter<MaskPoint>(),
            new JsonStringEnumConverter<MaskKind>(allowIntegerValues: false),
        },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { KeepStoredFormStable } },
    };

    public static string Serialize(EditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var valid = recipe.EnsureValid();
        return JsonSerializer.Serialize(valid with { SchemaVersion = valid.RequiredSchemaVersion }, Options);
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
            return problems.Count == 0 ? recipe with { SchemaVersion = EditRecipe.CurrentSchemaVersion } : null;
        }
        catch (JsonException ex)
        {
            error = "Not a readable recipe: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Leaves out what version 1 never wrote: the newer computed properties, and empty lists (a recipe without local
    /// adjustments, or a mask without clicks). The computed properties version 1 did write are kept, so its output is
    /// unchanged.
    /// </summary>
    private static void KeepStoredFormStable(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        string[] computed =
            info.Type == typeof(EditRecipe) ? [nameof(EditRecipe.HasLocalAdjustments), nameof(EditRecipe.RequiredSchemaVersion)]
            : info.Type == typeof(LocalAdjustment) ? [nameof(LocalAdjustment.HasNoEffect)]
            : info.Type == typeof(MaskReference) ? [nameof(MaskReference.DisplayName)]
            : [];
        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            if (computed.Contains(info.Properties[i].Name))
            {
                info.Properties.RemoveAt(i);
            }
        }

        foreach (var property in info.Properties)
        {
            if (info.Type == typeof(EditRecipe) && property.Name == nameof(EditRecipe.LocalAdjustments))
            {
                property.ShouldSerialize = (_, value) => value is ValueList<LocalAdjustment> { Count: > 0 };
            }
            else if (info.Type == typeof(MaskReference) && property.Name == nameof(MaskReference.Points))
            {
                property.ShouldSerialize = (_, value) => value is ValueList<MaskPoint> { Count: > 0 };
            }
        }
    }

    /// <summary>Reads and writes a <see cref="ValueList{T}"/> as a plain JSON array.</summary>
    private sealed class ValueListConverter<T> : JsonConverter<ValueList<T>>
    {
        public override ValueList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? []);

        public override void Write(Utf8JsonWriter writer, ValueList<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
