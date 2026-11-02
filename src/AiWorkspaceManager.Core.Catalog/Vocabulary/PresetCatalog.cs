using System.Text.Json;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>Presets in their curated order; file order is the reading order the UI keeps.</summary>
public sealed class PresetCatalog
{
    public static PresetCatalog Empty { get; } = new();

    public IReadOnlyList<string> Order { get; init; } = [];

    public IReadOnlyDictionary<string, PresetDefinition> Presets { get; init; } =
        new Dictionary<string, PresetDefinition>(StringComparer.Ordinal);

    public static PresetCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("presets", out var presets)
                || presets.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            var order = new List<string>();
            var definitions = new Dictionary<string, PresetDefinition>(StringComparer.Ordinal);
            foreach (var property in presets.EnumerateObject())
            {
                order.Add(property.Name);
                definitions[property.Name] = ParseDefinition(property.Value);
            }

            return new PresetCatalog { Order = order, Presets = definitions };
        }
        catch (JsonException)
        {
            return Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
    }

    private static PresetDefinition ParseDefinition(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return new PresetDefinition();
        }

        var description = body.TryGetProperty("description", out var descriptionValue)
            && descriptionValue.ValueKind == JsonValueKind.String
            ? descriptionValue.GetString() ?? string.Empty
            : string.Empty;
        var recommended = body.TryGetProperty("recommended", out var recommendedValue)
            && recommendedValue.ValueKind == JsonValueKind.True;

        var entries = new Dictionary<string, IReadOnlyList<PresetEntry>>(StringComparer.Ordinal);
        foreach (var key in ResolverDefaults.ManifestKeys.Values)
        {
            if (!body.TryGetProperty(key, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var parsed = new List<PresetEntry>();
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    parsed.Add(new PresetEntry(entry.GetString(), false));
                }
                else if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("all", out var all)
                    && all.ValueKind == JsonValueKind.True)
                {
                    parsed.Add(new PresetEntry(null, true));
                }
            }

            entries[key] = parsed;
        }

        return new PresetDefinition
        {
            Description = description,
            Recommended = recommended,
            Entries = entries,
        };
    }
}
