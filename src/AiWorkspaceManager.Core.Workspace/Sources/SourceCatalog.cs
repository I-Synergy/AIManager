using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Declared sources in resolver order, with the local directory each one reads from. Built-in
/// sources are ordered by their declaration, not by the order a manifest happened to be written.
/// </summary>
public sealed class SourceCatalog
{
    private readonly WorkspacePaths paths;
    private readonly ISourceFetcher fetcher;
    private readonly bool offline;

    public SourceCatalog(WorkspacePaths paths, ISourceFetcher fetcher, bool offline)
    {
        this.paths = paths;
        this.fetcher = fetcher;
        this.offline = offline;
    }

    public bool Offline => offline;

    /// <summary>[(name, entry)] for every declared source, the template source first.</summary>
    public static IReadOnlyList<(string Name, JsonObject Entry)> Sources(ManifestDocument manifest)
    {
        var sources = manifest.GetObject("sources");
        if (sources is null || sources.Count == 0)
        {
            return [(ResolverDefaults.TemplateSource, new JsonObject())];
        }

        var ordered = new List<(string Name, JsonObject Entry)>();
        if (sources[ResolverDefaults.TemplateSource] is JsonObject template)
        {
            ordered.Add((ResolverDefaults.TemplateSource, template));
        }
        else if (sources.ContainsKey(ResolverDefaults.TemplateSource))
        {
            ordered.Add((ResolverDefaults.TemplateSource, new JsonObject()));
        }

        var declared = new List<(string Name, JsonObject Entry)>();
        foreach (var (name, node) in sources)
        {
            if (name == ResolverDefaults.TemplateSource)
            {
                continue;
            }

            var entry = node as JsonObject ?? new JsonObject();
            if (entry["enabled"] is JsonValue enabled
                && enabled.TryGetValue<bool>(out var isEnabled)
                && !isEnabled)
            {
                continue;
            }

            declared.Add((name, entry));
        }

        declared.Sort((left, right) => BuiltinSources.IndexOf(left.Name).CompareTo(BuiltinSources.IndexOf(right.Name)));
        return ordered.Concat(declared).ToList();
    }

    public static JsonObject EntryNamed(ManifestDocument manifest, string name) =>
        Sources(manifest).FirstOrDefault(source => source.Name == name).Entry ?? new JsonObject();

    public static bool IsRemote(JsonObject entry)
    {
        var mode = entry["resolve"] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text.ToLowerInvariant()
            : "local";
        if (mode is not ("local" or "remote"))
        {
            throw new ResolverException($"source resolve must be 'local' or 'remote', not '{mode}'");
        }

        return mode == "remote";
    }

    public string SourceRoot(string name, JsonObject entry, bool refresh = false)
    {
        if (entry["path"] is JsonValue pathValue && pathValue.TryGetValue<string>(out var rawPath)
            && !string.IsNullOrEmpty(rawPath))
        {
            return Path.GetFullPath(Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(paths.Root, rawPath));
        }

        if (IsRemote(entry))
        {
            return Fetch(name, entry, refresh);
        }

        if (name == ResolverDefaults.TemplateSource)
        {
            return paths.Root;
        }

        return Fetch(name, entry, refresh);
    }

    public string TemplateRoot(ManifestDocument manifest) =>
        SourceRoot(ResolverDefaults.TemplateSource, EntryNamed(manifest, ResolverDefaults.TemplateSource));

    public static string SourceRef(ManifestDocument manifest) =>
        EntryNamed(manifest, ResolverDefaults.TemplateSource)["ref"] is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text
            : ResolverDefaults.LocalRef;

    public IReadOnlyList<CatalogSource> ToCatalogSources(
        ManifestDocument manifest,
        bool refresh = false,
        IReadOnlySet<string>? onlySources = null)
    {
        var result = new List<CatalogSource>();
        foreach (var (name, entry) in Sources(manifest))
        {
            if (onlySources is not null && !onlySources.Contains(name))
            {
                continue;
            }

            var root = SourceRoot(name, entry, refresh);
            result.Add(new CatalogSource(
                name,
                root,
                StringOf(entry, "repo") ?? ResolverDefaults.TemplateRepo,
                StringOf(entry, "ref") ?? ResolverDefaults.LocalRef,
                StringOf(entry, "license") ?? ResolverDefaults.License,
                ReadPaths(entry)));
        }

        return result;
    }

    /// <summary>Every tag on a repository, or a reason it could not be read.</summary>
    public (IReadOnlyList<string>? Tags, string? Error) RemoteTags(string repo)
    {
        if (string.IsNullOrEmpty(repo))
        {
            return (null, "source declares no repo");
        }

        var url = repo.Contains("://", StringComparison.Ordinal) || repo.StartsWith("git@", StringComparison.Ordinal)
            ? repo
            : $"https://github.com/{repo}.git";
        if (offline && !url.StartsWith("file://", StringComparison.Ordinal))
        {
            return (null, "offline");
        }

        return fetcher.RemoteTags(repo);
    }

    public static IReadOnlyList<SourceRow> Rows(ManifestDocument manifest) =>
        Sources(manifest).Select(source => new SourceRow(
            source.Name,
            StringOf(source.Entry, "repo") ?? ResolverDefaults.TemplateRepo,
            StringOf(source.Entry, "ref") ?? ResolverDefaults.LocalRef,
            IsRemote(source.Entry) ? "remote" : "local",
            StringOf(source.Entry, "license") ?? ResolverDefaults.License,
            source.Entry["allowHooks"] is JsonValue hooks && hooks.TryGetValue<bool>(out var allow) && allow,
            StringOf(source.Entry, "path"))).ToList();

    private string Fetch(string name, JsonObject entry, bool refresh)
    {
        var repo = StringOf(entry, "repo");
        if (string.IsNullOrEmpty(repo))
        {
            throw new ResolverException($"source '{name}' declares neither 'path' nor 'repo'");
        }

        var reference = StringOf(entry, "ref") ?? ResolverDefaults.LocalRef;
        if (reference == ResolverDefaults.LocalRef)
        {
            throw new ResolverException(
                $"source '{name}' must pin a ref (tag, branch or commit SHA); "
                + $"'{ResolverDefaults.LocalRef}' is reserved for the template source");
        }

        var url = repo.Contains("://", StringComparison.Ordinal) || repo.StartsWith("git@", StringComparison.Ordinal)
            ? repo
            : $"https://github.com/{repo}.git";
        if (offline && !url.StartsWith("file://", StringComparison.Ordinal))
        {
            throw new ResolverException(
                $"CATALOG_OFFLINE is set, so source '{name}' ({repo} @ {reference}) was not "
                + "fetched — unset it, or give that source a local \"path\"");
        }

        return fetcher.Fetch(name, repo, reference, refresh);
    }

    private static string? StringOf(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static Dictionary<string, IReadOnlyList<string>>? ReadPaths(JsonObject entry)
    {
        if (entry["paths"] is not JsonObject configured)
        {
            return null;
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, node) in configured)
        {
            if (node is JsonArray array)
            {
                result[key] = array
                    .Select(value => value is JsonValue item && item.TryGetValue<string>(out var text) ? text : null)
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToList();
            }
            else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                result[key] = [text];
            }
        }

        return result;
    }
}
