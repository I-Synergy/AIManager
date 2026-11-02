using System.Text.Json;
using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// The setup and maintenance commands the UI and activation use: bootstrap, scaffold, clients,
/// adopt, index and notice. `sync`/`update` call the same setup service for their side effects.
/// </summary>
public sealed class MaintenanceService
{
    private readonly WorkspaceQueries queries;
    private readonly WorkspaceSetupService setup;
    private readonly WorkspaceOperations operations;

    public MaintenanceService(
        WorkspaceQueries queries,
        WorkspaceSetupService setup,
        WorkspaceOperations operations)
    {
        this.queries = queries;
        this.setup = setup;
        this.operations = operations;
    }

    /// <summary>Set a repository up from the template in one call (activation's setup step).</summary>
    public OperationOutcome Bootstrap()
    {
        var existing = queries.LoadManifest();
        var manifest = existing ?? queries.StarterManifest().Bind(queries.Paths.ManifestPath);
        var addedSources = new List<string>();
        if (existing is null)
        {
            manifest.Save();
        }
        else
        {
            addedSources = MergeBuiltinSources(manifest);
            if (addedSources.Count > 0)
            {
                manifest.Save();
            }
        }

        setup.MigrateCatalogDirectory();
        manifest = queries.LoadManifest() ?? manifest;
        var sourceRoot = queries.Sources.TemplateRoot(manifest);

        var legacyRemoved = new List<string>();
        if (!WorkspacePaths.SamePath(sourceRoot, queries.Paths.Root))
        {
            var installed = queries.LoadLock().Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Path))
                .Select(entry => Path.Combine(queries.Paths.Root, entry.Path))
                .ToList();
            legacyRemoved = Materializer.RemoveLegacyArtifacts(queries.Paths, installed)["removed"];
        }

        var tooling = setup.InstallTooling(sourceRoot, overwrite: true, reference: SourceCatalog.SourceRef(manifest));
        _ = setup.InstallAgentConfig(manifest.Root);
        var scaffold = existing is null
            ? setup.InstallScaffold(sourceRoot)
            : new FileCopySummary([], []);
        var gitignore = setup.EnsureGitignore();
        var readme = new ProjectInfoService(queries.Paths).EnsureReadme();
        if (SourceCatalog.IsRemote(SourceCatalog.EntryNamed(manifest, ResolverDefaults.TemplateSource))
            && !File.Exists(queries.Paths.ProjectInfoPath))
        {
            var project = new ProjectInfoService(queries.Paths);
            _ = project.Describe(project.DetectedName(), string.Empty);
        }

        _ = new PlatformMirrorService(queries.Paths).Sync(manifest);
        var instructions = new InstructionDocumentService(queries).Update(manifest.Root);

        var selected = ResolverDefaults.Kinds.Sum(kind => manifest.GetStringArray(ResolverDefaults.ManifestKeys[kind]).Count);
        var payload = new BootstrapPayload(
            existing is null,
            queries.Paths.ManifestPath,
            selected,
            addedSources,
            new BootstrapToolingSummary(tooling.Copied, tooling.Skipped, tooling.Unchanged, tooling.Missing, tooling.Protected),
            new BootstrapScaffoldSummary(scaffold.Copied, scaffold.Skipped),
            legacyRemoved,
            gitignore,
            readme,
            instructions);

        var lines = new List<string> { $"wrote: {queries.Paths.ManifestPath} ({selected} selected item(s))" };
        if (addedSources.Count > 0)
        {
            lines.Add($"sources: added {string.Join(", ", addedSources)}");
        }

        lines.Add($"tooling: {tooling.Copied.Count} copied, {tooling.Missing.Count} missing");
        lines.Add($"scaffold: {scaffold.Copied.Count} copied, {scaffold.Skipped.Count} kept");
        if (legacyRemoved.Count > 0)
        {
            lines.Add($"legacy state: {legacyRemoved.Count} file(s) removed");
        }

        return new OperationOutcome(new CliResult(0, JoinLines(lines), string.Empty), payload);
    }

    /// <summary>Copy any missing supporting <c>.ai/</c> files on request.</summary>
    public OperationOutcome Scaffold()
    {
        var manifest = queries.ManifestOrStarter();
        var sourceRoot = queries.Sources.TemplateRoot(manifest);
        var summary = setup.InstallScaffold(sourceRoot);
        _ = setup.InstallAgentConfig(manifest.Root);
        var payload = new ScaffoldPayload(summary.Copied, summary.Skipped);
        return new OperationOutcome(
            new CliResult(
                0,
                $"scaffold: {summary.Copied.Count} copied, {summary.Skipped.Count} kept\n",
                string.Empty),
            payload);
    }

    /// <summary>Show or set which AI clients get platform folders.</summary>
    public OperationOutcome Clients(IReadOnlyList<string>? set = null)
    {
        var manifest = queries.ManifestOrStarter();
        if (set is null)
        {
            var current = ResolverDefaults.Clients
                .Where(client => SelectionEngine.Clients(manifest).Contains(client))
                .ToList();
            return new OperationOutcome(
                new CliResult(0, $"clients: {(current.Count > 0 ? string.Join(", ", current) : "(none)")}\n", string.Empty),
                new ClientsPayload(current));
        }

        var names = new List<string>();
        foreach (var raw in set)
        {
            var name = raw.Trim().ToLowerInvariant();
            if (!ResolverDefaults.Clients.Contains(name))
            {
                throw new ResolverException($"unknown client '{raw}' — choose from {string.Join(", ", ResolverDefaults.Clients)}");
            }

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        manifest.Set("clients", new JsonArray(names.Select(name => (JsonNode)name).ToArray()));
        manifest.Save();
        return new OperationOutcome(
            new CliResult(
                0,
                $"clients: {(names.Count > 0 ? string.Join(", ", names) : "(none)")}\n"
                + "run 'catalog.py sync' to add or remove the folders\n",
                string.Empty),
            new ClientsPayload(names));
    }

    /// <summary>Write <c>.ai/ai.json</c> describing what this repository already has.</summary>
    public OperationOutcome Adopt()
    {
        var path = queries.Paths.ManifestPath;
        if (File.Exists(path))
        {
            var existing = queries.LoadManifest() ?? ManifestDocument.Temporary(new JsonObject());
            var selected = ResolverDefaults.Kinds.Sum(kind => existing.GetStringArray(ResolverDefaults.ManifestKeys[kind]).Count);
            return new OperationOutcome(
                new CliResult(0, $"kept: {path} already exists\n", string.Empty),
                new AdoptPayload(false, path, selected));
        }

        var manifest = queries.StarterManifest().Bind(path);
        if (queries.HasLocalCatalog())
        {
            manifest.Set("materialize", "copy");
        }

        manifest.Save();
        var count = ResolverDefaults.Kinds.Sum(kind => manifest.GetStringArray(ResolverDefaults.ManifestKeys[kind]).Count);
        return new OperationOutcome(
            new CliResult(0, $"wrote: {path} ({count} selected item(s))\n", string.Empty),
            new AdoptPayload(true, path, count));
    }

    /// <summary>Copy the bundled vocabulary into this repository (the old install-tooling script).</summary>
    public OperationOutcome VendorTooling(bool overwrite = false)
    {
        var toolingRoot = queries.BundledToolingRoot;
        if (string.IsNullOrEmpty(toolingRoot) || !Directory.Exists(toolingRoot))
        {
            throw new ResolverException(
                "the bundled tooling is missing — rebuild the extension so tooling/ is packaged");
        }

        var copied = new List<string>();
        var skipped = new List<string>();
        foreach (var source in Directory.EnumerateFiles(toolingRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(toolingRoot, source).Replace('\\', '/');
            if (relative == "tooling.json")
            {
                continue;
            }

            var destination = relative.StartsWith("catalog/", StringComparison.Ordinal)
                ? ".ai/" + relative
                : relative;
            var target = Path.Combine(queries.Paths.Root, destination);
            if (!overwrite && File.Exists(target))
            {
                skipped.Add(destination);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, target, overwrite: true);
            copied.Add(destination);
        }

        var manifest = File.Exists(queries.Paths.ManifestPath) ? "kept" : "wrote";
        if (manifest == "wrote")
        {
            queries.StarterManifest().Bind(queries.Paths.ManifestPath).Save();
        }

        _ = setup.EnsureGitignore();
        var payload = new VendorToolingSummary(copied, skipped, manifest);
        return new OperationOutcome(
            new CliResult(
                0,
                $"vendor: {copied.Count} copied, {skipped.Count} skipped, manifest {manifest}\n",
                string.Empty),
            payload);
    }

    /// <summary>Build the catalog index cache, optionally verifying or writing the compact view.</summary>
    public OperationOutcome Index(string? outPath = null, bool verify = false, bool compact = false, string? compactOut = null)
    {
        var manifest = queries.LoadManifest() ?? queries.StarterManifest();
        var index = queries.BuildIndex(manifest);
        var output = string.IsNullOrEmpty(outPath) ? queries.Paths.IndexPath : outPath;
        if (!Path.IsPathRooted(output))
        {
            output = Path.Combine(queries.Paths.Root, output);
        }

        var existing = JsonFiles.ReadObject(output);
        var stale = existing is null || JsonFiles.Canonical(existing) != JsonFiles.Canonical(IndexNode(index));
        string? compactPath = null;
        var compactStale = false;
        string? compactText = null;
        if (compact)
        {
            compactPath = string.IsNullOrEmpty(compactOut)
                ? Path.Combine(Path.GetDirectoryName(output)!, "INDEX.md")
                : compactOut;
            if (!Path.IsPathRooted(compactPath))
            {
                compactPath = Path.Combine(queries.Paths.Root, compactPath);
            }

            int? installed = null;
            try
            {
                installed = queries.Engine(index).EffectiveSelection(manifest, strict: false).Count;
            }
            catch (ResolverException)
            {
                installed = null;
            }

            compactText = RenderCompact(index, installed);
            compactStale = !File.Exists(compactPath)
                || File.ReadAllText(compactPath) != compactText;
        }

        if (verify)
        {
            var code = stale || compactStale ? 1 : 0;
            var lines = code == 0
                ? $"index is up to date: {output} ({index.Items.Count} item(s))\n"
                : $"STALE: index is stale: {output} (run 'catalog.py index')\n";
            return new OperationOutcome(
                new CliResult(code, lines, string.Empty),
                new IndexPayload(stale || compactStale, false, output, index.Items.Count, compactPath));
        }

        if (stale)
        {
            JsonFiles.Write(output, IndexNode(index));
        }

        if (compactPath is not null && compactStale && compactText is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(compactPath)!);
            File.WriteAllText(compactPath, compactText);
        }

        var payload = new IndexPayload(false, stale, output, index.Items.Count, compactPath);
        var message = $"{(stale ? "wrote" : "unchanged")}: {output} ({index.Items.Count} item(s))\n";
        if (compactPath is not null)
        {
            message += $"compact: {compactPath}\n";
        }

        return new OperationOutcome(new CliResult(0, message, string.Empty), payload);
    }

    /// <summary>Write attribution for the third-party content this project installs.</summary>
    public OperationOutcome Notice(string outPath = "NOTICE.md", bool verify = false)
    {
        var manifest = queries.ManifestOrStarter();
        var index = queries.BuildIndex(manifest);
        var indexByKey = index.ByKey();
        var engine = queries.Engine(index);
        var selected = engine.EffectiveSelection(manifest, strict: false);
        var output = Path.IsPathRooted(outPath) ? outPath : Path.Combine(queries.Paths.Root, outPath);

        var rows = selected
            .Where(entry => indexByKey.ContainsKey((entry.Kind, entry.Id)))
            .Select(entry => indexByKey[(entry.Kind, entry.Id)])
            .OrderBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ToList();

        var lines = new List<string>
        {
            "# NOTICE",
            string.Empty,
            "Third-party skills and agents installed in this project, with their source,",
            "pinned ref, licence and upstream URL. Generated by",
            "`catalog.py notice` — do not edit by hand.",
            string.Empty,
            "Review the upstream of anything you install. Content in this catalog may be",
            "sourced from third parties and is included as-is.",
            string.Empty,
            "| Item | Kind | Source | Ref | License | Upstream |",
            "|---|---|---|---|---|---|",
        };
        lines.AddRange(rows.Select(item =>
            $"| `{ItemId.Display(item.Id)}` | {item.Kind} | {item.Source} | {item.Ref} | {item.License} | {item.Upstream} |"));
        var text = string.Join("\n", lines) + "\n";

        if (verify)
        {
            var existing = File.Exists(output) ? File.ReadAllText(output) : null;
            var stale = existing != text;
            return new OperationOutcome(
                new CliResult(stale ? 1 : 0, stale ? string.Empty : $"notice is up to date: {output} ({rows.Count} item(s))\n", stale ? $"STALE: {output} — run 'catalog.py notice'\n" : string.Empty),
                new NoticePayload(stale, output, rows.Count));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, text);
        return new OperationOutcome(
            new CliResult(0, $"wrote: {output} ({rows.Count} item(s))\n", string.Empty),
            new NoticePayload(false, output, rows.Count));
    }

    /// <summary>The CLI's bootstrap command; also used by a manual setup.</summary>
    public OperationOutcome Init(
        string? preset = null,
        bool all = false,
        IReadOnlyList<string>? ids = null,
        IReadOnlyList<string>? agents = null,
        string? repo = null,
        string? reference = null,
        string? path = null,
        string? materialize = null,
        bool force = false)
    {
        var manifestPath = queries.Paths.ManifestPath;
        var fresh = !File.Exists(manifestPath);
        if (!fresh && !force)
        {
            throw new ResolverException($"{Path.GetFileName(manifestPath)} already exists — use --force to replace it");
        }

        var presetName = all ? "Everything" : preset;
        if (string.IsNullOrEmpty(presetName) && (ids is null || ids.Count == 0) && (agents is null || agents.Count == 0))
        {
            throw new ResolverException("init needs --preset <name>, --all, or explicit ids");
        }

        var existing = queries.LoadManifest();
        var data = queries.DefaultManifest();
        if (!queries.HasLocalCatalog())
        {
            var sourceEntry = data.GetObject("sources")!["template"]!.AsObject();
            sourceEntry["repo"] = repo ?? ResolverDefaults.TemplateRepo;
            sourceEntry["ref"] = reference ?? "main";
            sourceEntry["resolve"] = "remote";
        }
        else
        {
            var sourceEntry = data.GetObject("sources")!["template"]!.AsObject();
            sourceEntry["repo"] = repo ?? ResolverDefaults.TemplateRepo;
            sourceEntry["ref"] = reference ?? ResolverDefaults.LocalRef;
        }

        if (!queries.Offline)
        {
            var sources = data.GetObject("sources")!;
            foreach (var name in BuiltinSources.Names)
            {
                if (!sources.ContainsKey(name))
                {
                    sources[name] = BuiltinSources.Create(name);
                }
            }
        }

        if (existing?.GetObject("sources") is { } existingSources)
        {
            var sources = data.GetObject("sources")!;
            foreach (var (name, node) in existingSources.ToList())
            {
                sources[name] = node?.DeepClone();
            }
        }

        var templateEntry = data.GetObject("sources")!["template"]!.AsObject();
        if (repo is not null)
        {
            templateEntry["repo"] = repo;
        }

        if (reference is not null)
        {
            templateEntry["ref"] = reference;
        }

        if (path is not null)
        {
            templateEntry["path"] = path;
        }

        data.Set("preset", presetName);
        data.Set("skills", new JsonArray((ids ?? []).Select(id => (JsonNode)id).ToArray()));
        data.Set("agents", new JsonArray((agents ?? []).Select(id => (JsonNode)id).ToArray()));
        var defaultMode = SourceCatalog.IsRemote(templateEntry) ? "copy" : "link";
        data.Set("materialize", materialize ?? defaultMode);

        var manifest = data.Bind(manifestPath);
        manifest.Save();

        if (fresh)
        {
            var sourceRoot = queries.Sources.TemplateRoot(manifest);
            _ = setup.InstallScaffold(sourceRoot);
            _ = setup.InstallAgentConfig(manifest.Root);
        }

        var sync = operations.Sync();
        return new OperationOutcome(
            sync.Result,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["init"] = true });
    }

    private List<string> MergeBuiltinSources(ManifestDocument manifest)
    {
        var added = new List<string>();
        if (queries.Offline)
        {
            return added;
        }

        var sources = manifest.GetObject("sources") ?? new JsonObject();
        if (manifest.GetObject("sources") is null)
        {
            manifest.Set("sources", sources);
        }

        foreach (var name in BuiltinSources.Names)
        {
            if (!sources.ContainsKey(name))
            {
                sources[name] = BuiltinSources.Create(name);
                added.Add(name);
            }
        }

        return added;
    }

    private static JsonNode IndexNode(CatalogIndex index) =>
        JsonSerializer.SerializeToNode(index, JsonFiles.Indented)!;

    private static string RenderCompact(CatalogIndex index, int? installed)
    {
        var lines = new List<string>
        {
            "# Catalog index",
            string.Empty,
            "Generated by `ai-workspace-manager-core index --compact`. Do not edit.",
            $"{index.Items.Count} item(s) available in the catalog"
            + (installed is not null ? $"; this project installs {installed}" : string.Empty)
            + ". Add one with `catalog.py add <id>`, then `catalog.py sync`.",
            string.Empty,
        };
        foreach (var item in index.Items)
        {
            var description = Frontmatter.OneLine(item.Description);
            if (description.Length > 140)
            {
                description = description[..137].TrimEnd() + "...";
            }

            lines.Add($"- `{ItemId.Display(item.Id)}` ({item.Kind}) — {description}");
        }

        return string.Join("\n", lines) + "\n";
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        var text = string.Join("\n", lines);
        return text.Length == 0 ? string.Empty : text + "\n";
    }
}
