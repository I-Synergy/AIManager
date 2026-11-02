using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>Per-source classification rules: an imported source maps to one bucket.</summary>
public sealed record SourceCategoryRule(
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("default")] string? Default);
