using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>One entry of <c>catalog/categories.json</c>.</summary>
public sealed record CategoryDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("description")] string? Description);
