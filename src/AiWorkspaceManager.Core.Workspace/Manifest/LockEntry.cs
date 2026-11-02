using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One resolved item in <c>.ai/ai.lock.json</c>.</summary>
public sealed class LockEntry : IItemEntry
{
    public LockEntry(JsonObject json)
    {
        Json = json;
    }

    public JsonObject Json { get; }

    public string Kind => Get("kind") ?? "skill";

    public string Id => Get("id") ?? string.Empty;

    public string Path => Get("path") ?? string.Empty;

    public string? SourcePath => null;

    public string? Sha256 => Get("sha256");

    public string? Source => Get("source");

    public string? Ref => Get("ref");

    public string? Materialize => Get("materialize");

    public string? Get(string key) =>
        Json.TryGetPropertyValue(key, out var node)
        && node is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>The lock shape written after an item is materialised.</summary>
    public static LockEntry Make(string kind, CatalogItem item, string materialize)
    {
        var json = new JsonObject
        {
            ["id"] = item.Id,
            ["kind"] = kind,
            ["source"] = item.Source,
            ["ref"] = item.Ref,
            ["path"] = item.Path,
            ["sha256"] = item.Sha256,
            ["files"] = item.Files,
            ["license"] = item.License,
            ["upstream"] = item.Upstream,
            ["materialize"] = materialize,
        };
        return new LockEntry(json);
    }
}
