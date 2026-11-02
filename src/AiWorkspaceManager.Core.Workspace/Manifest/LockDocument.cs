using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// The installed-state record. The lock is committed and compared by content hash; entries are
/// sorted by (kind, id) so a rewrite is stable.
/// </summary>
public sealed class LockDocument
{
    private LockDocument(List<LockEntry> entries, int lockVersion = 1)
    {
        Entries = entries;
        LockVersion = lockVersion;
    }

    public IReadOnlyList<LockEntry> Entries { get; }

    public int LockVersion { get; }

    public static LockDocument Empty { get; } = new([]);

    public static LockDocument FromEntries(IEnumerable<LockEntry> entries) => new(entries.ToList());

    public static LockDocument Load(string path)
    {
        var root = JsonFiles.ReadObject(path);
        if (root is null)
        {
            return Empty;
        }

        if (!root.TryGetPropertyValue("resolved", out var resolvedNode) || resolvedNode is not JsonArray resolved)
        {
            throw new ResolverException($"lock {path} has no 'resolved' array");
        }

        var entries = new List<LockEntry>();
        foreach (var node in resolved)
        {
            if (node is not JsonObject entry)
            {
                throw new ResolverException($"lock {path} is not a JSON object");
            }

            entries.Add(new LockEntry(entry));
        }

        var version = 1;
        if (root["lockVersion"] is JsonValue versionValue && versionValue.TryGetValue<int>(out var parsed))
        {
            version = parsed;
        }

        return new LockDocument(entries, version);
    }

    public IReadOnlyDictionary<(string Kind, string Id), LockEntry> ByKey() =>
        Entries.ToDictionary(entry => (entry.Kind, entry.Id));

    public static void Save(string path, IEnumerable<LockEntry> entries)
    {
        var sorted = entries
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToList();
        var root = new JsonObject
        {
            ["lockVersion"] = 1,
            ["resolved"] = new JsonArray(sorted.Select(entry => (JsonNode)entry.Json.DeepClone()).ToArray()),
        };
        JsonFiles.Write(path, root);
    }

    /// <summary>Stable form for "would the lock need to change?" comparisons.</summary>
    public static string Signature(LockDocument lockDocument)
    {
        var sorted = lockDocument.Entries
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => (JsonNode)entry.Json.DeepClone())
            .ToArray();
        var root = new JsonObject
        {
            ["lockVersion"] = lockDocument.LockVersion,
            ["resolved"] = new JsonArray(sorted),
        };
        return JsonFiles.Canonical(root);
    }
}
