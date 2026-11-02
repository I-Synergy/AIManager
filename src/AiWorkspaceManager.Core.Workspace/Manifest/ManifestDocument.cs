using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// The project-owned selection file, <c>.ai/ai.json</c>. Unknown keys are preserved: a newer
/// client may write a field this one does not understand, and rewriting the file must not drop it.
/// </summary>
public sealed class ManifestDocument
{
    private ManifestDocument(string? path, JsonObject root)
    {
        Path = path;
        Root = root;
    }

    public string? Path { get; }

    public JsonObject Root { get; }

    public static ManifestDocument? Load(string path)
    {
        var root = JsonFiles.ReadObject(path);
        return root is null ? null : new ManifestDocument(path, root);
    }

    /// <summary>An in-memory manifest, used for preset cards and selection expansion.</summary>
    public static ManifestDocument Temporary(JsonObject root) => new(null, root);

    public static ManifestDocument Temporary(string presetName) =>
        Temporary(new JsonObject { ["preset"] = presetName });

    public string? GetString(string key) =>
        Root.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    public JsonArray? GetArray(string key) =>
        Root.TryGetPropertyValue(key, out var node) ? node as JsonArray : null;

    public IReadOnlyList<string> GetStringArray(string key)
    {
        var array = GetArray(key);
        return array is null
            ? []
            : array
                .Select(node => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
                .Where(text => !string.IsNullOrEmpty(text))
                .Select(text => text!)
                .ToList();
    }

    public JsonObject? GetObject(string key) =>
        Root.TryGetPropertyValue(key, out var node) ? node as JsonObject : null;

    public bool Has(string key) => Root.ContainsKey(key);

    public void Set(string key, JsonNode? value) => Root[key] = value;

    public bool Remove(string key) => Root.Remove(key);

    public JsonArray EnsureArray(string key)
    {
        if (Root[key] is JsonArray array)
        {
            return array;
        }

        array = [];
        Root[key] = array;
        return array;
    }

    public void Save()
    {
        if (Path is null)
        {
            throw new InvalidOperationException("a temporary manifest cannot be saved");
        }

        JsonFiles.Write(Path, Root);
    }

    public ManifestDocument Clone() => new(Path, (JsonObject)Root.DeepClone());

    /// <summary>Binds a temporary manifest to the project file so the first write persists it.</summary>
    public ManifestDocument Bind(string path) => new(path, Root);
}
