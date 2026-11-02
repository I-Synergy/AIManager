using System.Text.Json.Serialization;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>The generated catalog index (<c>.ai/tmp/catalog-index.json</c>).</summary>
public sealed class CatalogIndex
{
    public const string Generator = "dotnet AiWorkspaceManager.Core index";

    [JsonPropertyName("indexVersion")]
    public int IndexVersion { get; init; } = 1;

    [JsonPropertyName("generatedBy")]
    public string GeneratedBy { get; init; } = Generator;

    [JsonPropertyName("shadowed")]
    public IReadOnlyList<ShadowedItem> Shadowed { get; init; } = [];

    [JsonPropertyName("items")]
    public IReadOnlyList<CatalogItem> Items { get; init; } = [];

    /// <summary>Keyed by (kind, id): a skill and an agent may share a name.</summary>
    public IReadOnlyDictionary<(string Kind, string Id), CatalogItem> ByKey() =>
        Items.ToDictionary(item => (item.Kind, item.Id));
}
