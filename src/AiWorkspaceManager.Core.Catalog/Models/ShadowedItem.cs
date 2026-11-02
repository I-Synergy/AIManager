using System.Text.Json.Serialization;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>An item a later source offered under a name an earlier source already owns.</summary>
public sealed record ShadowedItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("by")] string By,
    [property: JsonPropertyName("path")] string Path);
