using System.Text.Json;

using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Catalog;

/// <summary>
/// Walks every resolved source and builds the sorted catalog index. This is the read-only half of
/// the resolver: no manifest, no lock, no writes.
/// </summary>
public sealed class CatalogIndexBuilder
{
    private readonly IReadOnlyList<CatalogSource> sources;
    private readonly CategoryVocabulary vocabulary;
    private readonly Action<string>? note;

    public CatalogIndexBuilder(
        IEnumerable<CatalogSource> sources,
        CategoryVocabulary? vocabulary = null,
        Action<string>? note = null)
    {
        this.sources = sources.ToList();
        this.vocabulary = vocabulary ?? CategoryVocabulary.Empty;
        this.note = note;
    }

    public CatalogIndex Build()
    {
        var entries = new List<BuildEntry>();
        var seen = new HashSet<(string Kind, string Id)>();
        var materialised = new Dictionary<(string Kind, string Name), string>();
        var shadowed = new List<ShadowedItem>();

        foreach (var source in sources)
        {
            foreach (var kind in ResolverDefaults.Kinds)
            {
                foreach (var entry in EnumerateEntries(kind, source))
                {
                    var document = KindDocument(kind, entry);
                    if (document is null)
                    {
                        continue;
                    }

                    var name = ItemName(kind, entry);
                    if (name is null)
                    {
                        continue;
                    }

                    var sourceRelative = RelativeToSource(source.Root, entry);
                    var installedPath =
                        $".ai/{ResolverDefaults.KindDirectories[kind]}/{name}{ResolverDefaults.KindSuffixes[kind]}";

                    var raw = File.ReadAllText(document);
                    var (description, declared) = kind is "plugin" or "hook"
                        ? ManifestMetadata(kind, raw)
                        : ItemMetadata(kind, raw);

                    var itemId = ItemId.Make(source.Name, name);
                    if (!seen.Add((kind, itemId)))
                    {
                        throw new ResolverException(
                            $"duplicate catalog id '{itemId}' for kind {kind} at {installedPath}");
                    }

                    if (materialised.TryGetValue((kind, name), out var previous)
                        && !string.Equals(previous, itemId, StringComparison.Ordinal))
                    {
                        shadowed.Add(new ShadowedItem(itemId, previous, installedPath));
                        continue;
                    }

                    materialised[(kind, name)] = itemId;
                    var (hash, count) = ContentHash.ItemHash(entry);
                    var (category, tags) = Classify(source.Name, name, raw, kind);
                    entries.Add(new BuildEntry(
                        new CatalogItem
                        {
                            Id = itemId,
                            Source = source.Name,
                            Kind = kind,
                            Name = name,
                            Path = installedPath,
                            SourcePath = sourceRelative,
                            Description = description,
                            Category = category,
                            Tags = tags,
                            Ref = source.Ref,
                            Sha256 = hash,
                            Files = count,
                            License = source.License,
                            Upstream = $"https://github.com/{source.Repo}/tree/{source.Ref}/{sourceRelative}",
                        },
                        declared));
                }
            }
        }

        ReportShadowed(shadowed);

        entries.Sort((left, right) =>
        {
            var compared = string.CompareOrdinal(left.Item.Id, right.Item.Id);
            return compared != 0 ? compared : string.CompareOrdinal(left.Item.Kind, right.Item.Kind);
        });

        var idsByName = entries.ToDictionary(
            entry => (entry.Item.Kind, entry.Item.Name),
            entry => entry.Item.Id);
        var items = entries
            .Select(entry => CopyWithRequires(entry.Item, entry.Declared, idsByName))
            .ToList();

        return new CatalogIndex { Shadowed = shadowed, Items = items };
    }

    private static IEnumerable<string> EnumerateEntries(string kind, CatalogSource source)
    {
        var directories = SourceKindDirectories(kind, source);
        var entries = new List<string>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            entries.AddRange(Directory.EnumerateFileSystemEntries(directory)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal));
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.') || string.Equals(name, "__pycache__", StringComparison.Ordinal))
            {
                continue;
            }

            yield return entry;
        }
    }

    private static List<string> SourceKindDirectories(string kind, CatalogSource source)
    {
        if (source.Paths is { } configured)
        {
            var key = configured.ContainsKey(kind)
                ? kind
                : configured.ContainsKey(ResolverDefaults.KindDirectories[kind])
                    ? ResolverDefaults.KindDirectories[kind]
                    : null;
            if (key is not null)
            {
                return (configured[key] ?? [])
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select(part => System.IO.Path.Combine(source.Root, part))
                    .ToList();
            }
        }

        var dotted = System.IO.Path.Combine(source.Root, ".ai", ResolverDefaults.KindDirectories[kind]);
        if (source.Name == ResolverDefaults.TemplateSource || Directory.Exists(dotted))
        {
            return [dotted];
        }

        return [System.IO.Path.Combine(source.Root, ResolverDefaults.KindDirectories[kind])];
    }

    private static string? KindDocument(string kind, string entry)
    {
        switch (kind)
        {
            case "skill":
                {
                    var document = Path.Combine(entry, "SKILL.md");
                    return Directory.Exists(entry) && File.Exists(document) ? document : null;
                }

            case "plugin":
                {
                    var document = Path.Combine(entry, "plugin.json");
                    return Directory.Exists(entry) && File.Exists(document) ? document : null;
                }

            case "hook":
                {
                    var document = Path.Combine(entry, "hooks.json");
                    return Directory.Exists(entry) && File.Exists(document) ? document : null;
                }

            default:
                {
                    if (!File.Exists(entry) || !entry.EndsWith(".md", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    var name = Path.GetFileName(entry);
                    var readme = string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase);
                    return kind switch
                    {
                        "agent" => name.EndsWith(".instructions.md", StringComparison.Ordinal)
                            || name.EndsWith(".prompt.md", StringComparison.Ordinal)
                                ? null
                                : entry,
                        "instruction" => name.EndsWith(".agent.md", StringComparison.Ordinal)
                            || name.EndsWith(".prompt.md", StringComparison.Ordinal)
                            || readme
                                ? null
                                : entry,
                        "prompt" => name.EndsWith(".instructions.md", StringComparison.Ordinal)
                            || name.EndsWith(".agent.md", StringComparison.Ordinal)
                            || readme
                                ? null
                                : entry,
                        _ => null,
                    };
                }
        }
    }

    private static string? ItemName(string kind, string entry)
    {
        var fileName = Path.GetFileName(entry);
        switch (kind)
        {
            case "skill":
            case "plugin":
            case "hook":
                return fileName;

            case "agent":
                {
                    var name = fileName[..^".md".Length];
                    return name.EndsWith(".agent", StringComparison.Ordinal)
                        ? name[..^".agent".Length]
                        : name;
                }

            case "instruction":
                return fileName.EndsWith(".instructions.md", StringComparison.Ordinal)
                    ? fileName[..^".instructions.md".Length]
                    : fileName[..^".md".Length];

            case "prompt":
                return fileName.EndsWith(".prompt.md", StringComparison.Ordinal)
                    ? fileName[..^".prompt.md".Length]
                    : fileName[..^".md".Length];

            default:
                return null;
        }
    }

    private static (string Description, IReadOnlyList<string> Declared) ItemMetadata(string kind, string raw)
    {
        var meta = Frontmatter.Parse(raw);
        var description = Frontmatter.OneLine(meta.GetString("description"));
        IReadOnlyList<string> declared = kind == "agent"
            ? meta.GetNameList("skills")
            : meta.GetNameList("requires");
        return (description, declared);
    }

    private static (string Description, IReadOnlyList<string> Declared) ManifestMetadata(string kind, string raw)
    {
        string name = string.Empty;
        string description = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("name", out var nameValue)
                    && nameValue.ValueKind == JsonValueKind.String)
                {
                    name = nameValue.GetString() ?? string.Empty;
                }

                if (document.RootElement.TryGetProperty("description", out var descriptionValue)
                    && descriptionValue.ValueKind == JsonValueKind.String)
                {
                    description = descriptionValue.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // A broken manifest is reported by the validator, not by the index.
        }

        _ = name;
        return (Frontmatter.OneLine(description),
            kind == "plugin" ? PluginMembers(raw) : []);
    }

    private static List<string> PluginMembers(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("extensions", out var extensions)
                || extensions.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var names = new List<string>();
            foreach (var block in extensions.EnumerateObject())
            {
                if (block.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var key in new[] { "agents", "skills" })
                {
                    if (!block.Value.TryGetProperty(key, out var paths)
                        || paths.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var path in paths.EnumerateArray())
                    {
                        if (path.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        var cleaned = (path.GetString() ?? string.Empty)
                            .Trim()
                            .TrimEnd('/')
                            .Split('/')[^1];
                        foreach (var suffix in new[] { ".agent.md", ".instructions.md", ".md" })
                        {
                            if (cleaned.EndsWith(suffix, StringComparison.Ordinal))
                            {
                                cleaned = cleaned[..^suffix.Length];
                                break;
                            }
                        }

                        if (cleaned.Length > 0)
                        {
                            names.Add(cleaned);
                        }
                    }
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private (string Category, IReadOnlyList<string> Tags) Classify(
        string sourceName,
        string name,
        string raw,
        string kind)
    {
        var meta = kind is "plugin" or "hook" ? Frontmatter.Empty : Frontmatter.Parse(raw);
        var tags = meta.GetNameList("tags");
        var defined = vocabulary.Ids().ToHashSet(StringComparer.Ordinal);

        if (vocabulary.Sources.TryGetValue(sourceName, out var rules)
            && !string.IsNullOrWhiteSpace(rules.Category))
        {
            return (rules.Category.Trim(), tags);
        }

        var declared = meta.GetString("category")?.Trim();
        if (!string.IsNullOrEmpty(declared))
        {
            return (declared, tags.Where(tag => tag != declared).ToList());
        }

        foreach (var tag in tags)
        {
            if (defined.Contains(tag))
            {
                return (tag, tags.Where(entry => entry != tag).ToList());
            }
        }

        var fallback = rules?.Default;
        return (string.IsNullOrWhiteSpace(fallback) ? "uncategorised" : fallback, tags);
    }

    private static string RelativeToSource(string root, string entry)
    {
        var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
        return relative.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(entry) : relative;
    }

    private static CatalogItem CopyWithRequires(
        CatalogItem item,
        IReadOnlyList<string> declared,
        Dictionary<(string Kind, string Name), string> idsByName)
    {
        var requires = declared
            .Select(name => idsByName.TryGetValue(("skill", name), out var id)
                ? id
                : ItemId.Make(item.Source, name))
            .ToList();

        return new CatalogItem
        {
            Id = item.Id,
            Source = item.Source,
            Kind = item.Kind,
            Name = item.Name,
            Path = item.Path,
            SourcePath = item.SourcePath,
            Description = item.Description,
            Category = item.Category,
            Tags = item.Tags,
            Ref = item.Ref,
            Sha256 = item.Sha256,
            Files = item.Files,
            License = item.License,
            Upstream = item.Upstream,
            Requires = requires,
        };
    }

    private void ReportShadowed(List<ShadowedItem> shadowed)
    {
        if (shadowed.Count == 0 || note is null)
        {
            return;
        }

        var shown = string.Join(", ", shadowed.Take(5).Select(entry => entry.Id));
        var more = shadowed.Count <= 5 ? string.Empty : $" and {shadowed.Count - 5} more";
        note($"note: {shadowed.Count} item(s) shadowed by a same-named item from an earlier source: {shown}{more}");
    }

    private sealed record BuildEntry(CatalogItem Item, IReadOnlyList<string> Declared);
}
