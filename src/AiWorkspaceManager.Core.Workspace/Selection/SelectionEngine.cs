using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Manifest → declared selection → dependency closure. This is the rule engine behind browse,
/// status, sync, update and every mutation; ported from <c>_selection</c>, <c>_closure</c> and
/// <c>_preset_selection</c> in the Python resolver.
/// </summary>
public sealed class SelectionEngine
{
    private readonly IReadOnlyDictionary<(string Kind, string Id), CatalogItem> indexByKey;
    private readonly PresetCatalog presets;

    public SelectionEngine(
        IReadOnlyDictionary<(string Kind, string Id), CatalogItem> indexByKey,
        PresetCatalog presets)
    {
        this.indexByKey = indexByKey;
        this.presets = presets;
    }

    public static string CanonicalPresetName(string name)
    {
        var text = name.Trim();
        return text == "full" ? "Everything" : text;
    }

    /// <summary>Active presets in order: the legacy singleton first, then the additive list.</summary>
    public static IReadOnlyList<string> PresetNames(ManifestDocument manifest)
    {
        var raw = new List<string>();
        var legacy = manifest.GetString("preset");
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            raw.Add(legacy);
        }

        raw.AddRange(manifest.GetStringArray("presets").Where(name => !string.IsNullOrWhiteSpace(name)));

        var names = new List<string>();
        foreach (var name in raw)
        {
            var canonical = CanonicalPresetName(name);
            if (!names.Contains(canonical))
            {
                names.Add(canonical);
            }
        }

        return names;
    }

    public IReadOnlyList<string> ValidatePresetNames(IEnumerable<string> names)
    {
        var canonical = new List<string>();
        foreach (var raw in names)
        {
            var name = CanonicalPresetName(raw);
            if (!presets.Presets.ContainsKey(name))
            {
                var close = CloseMatches(name, presets.Presets.Keys, 3);
                var hint = close.Count > 0 ? $" (did you mean: {string.Join(", ", close)})" : string.Empty;
                throw new ResolverException($"unknown preset '{name}'{hint} — see catalog/presets.json");
            }

            if (!canonical.Contains(name))
            {
                canonical.Add(name);
            }
        }

        return canonical;
    }

    /// <summary>Expands every active preset into one {kind: [id]} selection.</summary>
    public Dictionary<string, List<string>> PresetSelection(IEnumerable<string> names, bool strict = true)
    {
        var expanded = ResolverDefaults.Kinds.ToDictionary(kind => kind, _ => new List<string>(), StringComparer.Ordinal);
        var validated = ValidatePresetNames(names);
        if (validated.Count == 0)
        {
            return expanded;
        }

        foreach (var name in validated)
        {
            var body = presets.Presets[name];
            foreach (var kind in ResolverDefaults.Kinds)
            {
                var ids = expanded[kind];
                var manifestKey = ResolverDefaults.ManifestKeys[kind];
                if (!body.Entries.TryGetValue(manifestKey, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry.All)
                    {
                        foreach (var key in indexByKey.Keys
                            .Where(key => key.Kind == kind)
                            .OrderBy(key => key.Kind, StringComparer.Ordinal)
                            .ThenBy(key => key.Id, StringComparer.Ordinal))
                        {
                            if (!ids.Contains(key.Id))
                            {
                                ids.Add(key.Id);
                            }
                        }

                        continue;
                    }

                    var itemId = ItemId.Canonical(entry.Id ?? string.Empty);
                    if (strict && !indexByKey.ContainsKey((kind, itemId)))
                    {
                        throw new ResolverException(
                            $"preset '{name}' references unknown {kind} '{entry.Id}'");
                    }

                    if (!ids.Contains(itemId))
                    {
                        ids.Add(itemId);
                    }
                }
            }
        }

        return expanded;
    }

    /// <summary>Preset ids only — what a card would add, with no manifest list and no excludes.</summary>
    public List<(string Kind, string Id)> PresetSelected(string name, bool strict = false)
    {
        var expanded = PresetSelection([name], strict);
        return ResolverDefaults.Kinds
            .SelectMany(kind => expanded[kind].Select(id => (Kind: kind, Id: id)))
            .ToList();
    }

    /// <summary>Expands the manifest into an ordered, de-duplicated [(kind, id)] list.</summary>
    public List<(string Kind, string Id)> Selection(ManifestDocument manifest, bool strict = true)
    {
        var excludes = Excludes(manifest);
        var presetIds = PresetSelection(PresetNames(manifest), strict);
        var selected = new List<(string Kind, string Id)>();

        foreach (var kind in ResolverDefaults.Kinds)
        {
            var manifestKey = ResolverDefaults.ManifestKeys[kind];
            var rawIds = new List<string>(presetIds[kind]);
            rawIds.AddRange(manifest.GetStringArray(manifestKey));

            foreach (var raw in rawIds)
            {
                var itemId = ResolveManifestId(kind, raw);
                if (!indexByKey.ContainsKey((kind, itemId)) && strict)
                {
                    var other = kind == "skill" ? "agent" : "skill";
                    if (indexByKey.ContainsKey((other, itemId)))
                    {
                        throw new ResolverException(
                            $"'{ItemId.Display(itemId)}' is an {other}, but it is listed under "
                            + $"'{manifestKey}' in ai.json");
                    }

                    throw new ResolverException(
                        $"unknown id '{raw}' — not in the catalog "
                        + "(run 'catalog.py index' if the tree changed)");
                }

                if (excludes.Contains(itemId))
                {
                    continue;
                }

                if (!selected.Contains((kind, itemId)))
                {
                    selected.Add((kind, itemId));
                }
            }
        }

        return selected;
    }

    /// <summary>The selection plus everything it requires, dependencies first.</summary>
    public List<(string Kind, string Id)> Closure(IEnumerable<(string Kind, string Id)> selection)
    {
        var ordered = new List<(string Kind, string Id)>();
        var done = new HashSet<(string Kind, string Id)>();
        var visiting = new List<(string Kind, string Id)>();
        var missing = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        void Visit(string kind, string itemId, string? requiredBy)
        {
            var key = (Kind: kind, Id: itemId);
            if (done.Contains(key))
            {
                return;
            }

            if (visiting.Contains(key))
            {
                var chain = string.Join(
                    " -> ",
                    visiting.Append(key).Select(entry => ItemId.Display(entry.Id)));
                throw new ResolverException($"dependency cycle in the catalog: {chain}");
            }

            if (!indexByKey.TryGetValue(key, out var item))
            {
                if (requiredBy is not null)
                {
                    if (!missing.TryGetValue(itemId, out var whoNeeds))
                    {
                        whoNeeds = new SortedSet<string>(StringComparer.Ordinal);
                        missing[itemId] = whoNeeds;
                    }

                    whoNeeds.Add(requiredBy);
                }

                return;
            }

            visiting.Add(key);
            foreach (var dependency in item.Requires)
            {
                var dependencyKind = indexByKey.ContainsKey(("skill", dependency)) ? "skill" : "agent";
                Visit(dependencyKind, dependency, itemId);
            }

            visiting.RemoveAt(visiting.Count - 1);
            done.Add(key);
            ordered.Add(key);
        }

        foreach (var (kind, id) in selection)
        {
            Visit(kind, id, null);
        }

        if (missing.Count > 0)
        {
            var detail = string.Join(
                "; ",
                missing.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{ItemId.Display(pair.Key)} (required by "
                        + $"{string.Join(", ", pair.Value.Select(ItemId.Display))})"));
            throw new ResolverException($"the catalog declares dependencies that do not exist: {detail}");
        }

        return ordered;
    }

    public List<(string Kind, string Id)> EffectiveSelection(ManifestDocument manifest, bool strict = true) =>
        Closure(Selection(manifest, strict));

    /// <summary>Normalises a manifest entry, healing a legacy bare third-party id.</summary>
    public string ResolveManifestId(string kind, string raw)
    {
        var text = raw.Trim();
        var itemId = ItemId.Canonical(text);
        if (text.Contains(':', StringComparison.Ordinal) || indexByKey.ContainsKey((kind, itemId)))
        {
            return itemId;
        }

        var matches = BareMatches(text, kind);
        return matches.Count == 1 ? matches[0] : itemId;
    }

    /// <summary>Catalog ids whose manifest name is <paramref name="name"/>, optionally of one kind.</summary>
    public IReadOnlyList<string> BareMatches(string name, string? kind = null) =>
        indexByKey.Keys
            .Where(key => (kind is null || key.Kind == kind) && ItemId.Split(key.Id).Name == name)
            .Select(key => key.Id)
            .ToList();

    /// <summary>Selection ids the user froze: update and sync never overwrite them.</summary>
    public HashSet<string> LockedIds(ManifestDocument manifest)
    {
        var frozen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in manifest.GetStringArray("locked"))
        {
            var text = raw.Trim();
            var itemId = ItemId.SoftCanonical(text);
            if (itemId is null)
            {
                continue;
            }

            if (!text.Contains(':', StringComparison.Ordinal)
                && !indexByKey.Keys.Any(key => key.Id == itemId))
            {
                var matches = BareMatches(text);
                if (matches.Count == 1)
                {
                    itemId = matches[0];
                }
            }

            frozen.Add(itemId);
        }

        return frozen;
    }

    public HashSet<string> Excludes(ManifestDocument manifest)
    {
        var excludes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in manifest.GetStringArray("exclude"))
        {
            var text = raw.Trim();
            var normalized = ItemId.SoftCanonical(text);
            if (normalized is null)
            {
                continue;
            }

            if (!text.Contains(':', StringComparison.Ordinal)
                && !indexByKey.Keys.Any(key => key.Id == normalized))
            {
                var matches = BareMatches(text);
                if (matches.Count == 1)
                {
                    normalized = matches[0];
                }
            }

            excludes.Add(normalized);
        }

        return excludes;
    }

    /// <summary>The selected AI clients; a missing field means every legacy client.</summary>
    public static HashSet<string> Clients(ManifestDocument manifest)
    {
        if (!manifest.Has("clients") || manifest.Root["clients"] is null)
        {
            return ResolverDefaults.Clients.ToHashSet(StringComparer.Ordinal);
        }

        if (manifest.Root["clients"] is not System.Text.Json.Nodes.JsonArray array)
        {
            throw new ResolverException("clients must be a list of 'claude', 'copilot' and/or 'reasonix'");
        }

        var names = array
            .Where(node => node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out _))
            .Select(node => node!.GetValue<string>().Trim().ToLowerInvariant())
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = names.Except(ResolverDefaults.Clients).OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            throw new ResolverException(
                $"unknown client(s): {string.Join(", ", unknown)} — "
                + $"choose from {string.Join(", ", ResolverDefaults.Clients)}");
        }

        return names;
    }

    /// <summary>Hooks run scripts, so installing one needs per-source opt-in.</summary>
    public void CheckHookGate(
        ManifestDocument manifest,
        IEnumerable<(string Kind, string Id)> selected,
        SourceCatalog sources,
        WorkspacePaths paths)
    {
        var blocked = new List<(string Id, string Source)>();
        foreach (var (kind, itemId) in selected)
        {
            if (kind != "hook")
            {
                continue;
            }

            var source = indexByKey.TryGetValue((kind, itemId), out var item)
                ? item.Source
                : ResolverDefaults.TemplateSource;
            var entry = SourceCatalog.EntryNamed(manifest, source);
            var allowed = entry["allowHooks"] is System.Text.Json.Nodes.JsonValue value
                && value.TryGetValue<bool>(out var allow)
                && allow;
            if (allowed)
            {
                continue;
            }

            if (!WorkspacePaths.SamePath(sources.SourceRoot(source, entry), paths.Root))
            {
                blocked.Add((itemId, source));
            }
        }

        if (blocked.Count == 0)
        {
            return;
        }

        var detail = string.Join(
            ", ",
            blocked.Take(5).Select(entry => $"{ItemId.Display(entry.Id)} (source '{entry.Source}')"));
        var more = blocked.Count <= 5 ? string.Empty : $" and {blocked.Count - 5} more";
        throw new ResolverException(
            $"refusing to install hooks: {detail}{more} — add \"allowHooks\": true to that "
            + "source in ai.json to accept them");
    }

    private static List<string> CloseMatches(string value, IEnumerable<string> candidates, int limit)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Score: Similarity(value, candidate)))
            .Where(pair => pair.Score >= 0.6)
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Candidate, StringComparer.Ordinal)
            .Take(limit)
            .Select(pair => pair.Candidate)
            .ToList();
    }

    /// <summary>A normalised edit distance in [0, 1], used only for suggestions.</summary>
    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 1;
        }

        var distance = Levenshtein(left, right);
        return 1.0 - ((double)distance / Math.Max(left.Length, right.Length));
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
