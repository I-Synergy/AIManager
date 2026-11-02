using System.Text.Json.Serialization;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>One row of the catalog index — and the shape the browse payload is built from.</summary>
public sealed class CatalogItem : IItemEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Where the item is installed (always under <c>.ai/</c>).</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Where the item is read from inside its source.</summary>
    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = "uncategorised";

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    [JsonPropertyName("ref")]
    public string Ref { get; init; } = ResolverDefaults.LocalRef;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("files")]
    public int Files { get; init; }

    [JsonPropertyName("license")]
    public string License { get; init; } = ResolverDefaults.License;

    [JsonPropertyName("upstream")]
    public string Upstream { get; init; } = string.Empty;

    [JsonPropertyName("requires")]
    public IReadOnlyList<string> Requires { get; init; } = [];
}
