using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// JSON files the resolver writes. The formatting matches the Python resolver: two-space indent,
/// UTF-8 without a BOM, raw non-ASCII (not <c>\uXXXX</c>), and a trailing newline. The manifest is
/// committed, so a gratuitous reformat would show up as a whole-file diff.
/// </summary>
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The wire shape the TypeScript host expects: camelCase member names.</summary>
    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reads a JSON object; a missing file is null, malformed JSON throws.</summary>
    public static JsonObject? ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(string path, JsonNode node)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var text = node.ToJsonString(Indented) + "\n";
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(Indented);

    /// <summary>
    /// Sorted-key JSON, used only to compare two documents. Python's <c>json.dumps(sort_keys=True)</c>
    /// is the model; the exact separators do not matter as long as both sides use this method.
    /// </summary>
    public static string Canonical(JsonNode? node)
    {
        var sorted = Sort(node);
        return sorted?.ToJsonString(Compact) ?? "null";
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                {
                    var result = new JsonObject();
                    foreach (var property in jsonObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        result[property.Key] = Sort(property.Value);
                    }

                    return result;
                }

            case JsonArray jsonArray:
                {
                    var result = new JsonArray();
                    foreach (var entry in jsonArray)
                    {
                        result.Add(Sort(entry));
                    }

                    return result;
                }

            default:
                return node?.DeepClone();
        }
    }
}
