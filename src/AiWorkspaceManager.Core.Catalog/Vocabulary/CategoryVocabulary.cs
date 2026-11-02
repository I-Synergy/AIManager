using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>The controlled vocabulary plus per-source rules for items that declare none.</summary>
public sealed class CategoryVocabulary
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static CategoryVocabulary Empty { get; } = new();

    [JsonPropertyName("categories")]
    public IReadOnlyList<CategoryDefinition> Categories { get; init; } = [];

    [JsonPropertyName("sources")]
    public IReadOnlyDictionary<string, SourceCategoryRule> Sources { get; init; } =
        new Dictionary<string, SourceCategoryRule>(StringComparer.Ordinal);

    public IReadOnlyList<string> Ids() =>
        Categories.Where(entry => !string.IsNullOrEmpty(entry.Id)).Select(entry => entry.Id).ToList();

    /// <summary>Loads a vocabulary file; a missing or malformed file yields the empty vocabulary.</summary>
    public static CategoryVocabulary Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CategoryVocabulary>(json, Options) ?? Empty;
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
}
