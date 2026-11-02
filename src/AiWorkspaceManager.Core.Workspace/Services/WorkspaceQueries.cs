using System.Diagnostics;
using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Read paths over one workspace: manifest, index, selection, status, browse, updates and
/// releases. The Python resolver called these <c>cmd_browse</c>, <c>cmd_status</c> and friends.
/// </summary>
public sealed class WorkspaceQueries
{
    private readonly WorkspacePaths paths;
    private readonly SourceCatalog sources;
    private CategoryVocabulary? categories;
    private PresetCatalog? presets;

    public WorkspaceQueries(
        WorkspacePaths paths,
        ISourceFetcher fetcher,
        bool offline,
        string? bundledToolingRoot = null)
    {
        this.paths = paths;
        sources = new SourceCatalog(paths, fetcher, offline);
        BundledToolingRoot = bundledToolingRoot;
    }

    public WorkspacePaths Paths => paths;

    public SourceCatalog Sources => sources;

    public string? BundledToolingRoot { get; }

    public bool Offline => sources.Offline;

    public ManifestDocument? LoadManifest()
    {
        if (!File.Exists(paths.ManifestPath))
        {
            return null;
        }

        var manifest = ManifestDocument.Load(paths.ManifestPath);
        if (manifest is null)
        {
            throw new ResolverException($"manifest {paths.ManifestPath} is not a JSON object");
        }

        return manifest;
    }

    public ManifestDocument ManifestOrStarter() => LoadManifest() ?? StarterManifest();

    public ManifestDocument StarterManifest()
    {
        var data = DefaultManifest();
        if (HasLocalCatalog())
        {
            var local = LocalSelection();
            foreach (var (kind, names) in local)
            {
                data.Set(
                    ResolverDefaults.ManifestKeys[kind],
                    new JsonArray(names.Select(name => (JsonNode)name).ToArray()));
            }
        }
        else
        {
            var template = data.GetObject("sources")!["template"]!.AsObject();
            template["ref"] = "main";
            template["resolve"] = "remote";
            data.Set("materialize", "copy");
        }

        if (!Offline)
        {
            var declared = data.GetObject("sources")!;
            foreach (var name in BuiltinSources.Names)
            {
                if (!declared.ContainsKey(name))
                {
                    declared[name] = BuiltinSources.Create(name);
                }
            }
        }

        data.Remove("$schema");
        return data;
    }

    public ManifestDocument DefaultManifest()
    {
        var template = new JsonObject
        {
            ["repo"] = ResolverDefaults.TemplateRepo,
            ["ref"] = ResolverDefaults.LocalRef,
        };
        var data = new JsonObject
        {
            ["$schema"] = ManifestSchema(),
            ["version"] = 1,
            ["sources"] = new JsonObject { [ResolverDefaults.TemplateSource] = template },
            ["clients"] = new JsonArray(JsonValue.Create("claude")),
            ["presets"] = new JsonArray(),
        };
        foreach (var kind in ResolverDefaults.Kinds)
        {
            data[ResolverDefaults.ManifestKeys[kind]] = new JsonArray();
        }

        data["materialize"] = "link";
        data["exclude"] = new JsonArray();
        data["locked"] = new JsonArray();
        return ManifestDocument.Temporary(data);
    }

    public string ManifestSchema()
    {
        var rootCatalog = Path.Combine(paths.Root, "catalog");
        var aiCatalog = Path.Combine(paths.AiDirectory, "catalog");
        return Directory.Exists(rootCatalog) && !Directory.Exists(aiCatalog)
            ? "../catalog/schemas/ai.schema.json"
            : "catalog/schemas/ai.schema.json";
    }

    /// <summary>True when this project carries its own skills/agents/… tree.</summary>
    public bool HasLocalCatalog() =>
        ResolverDefaults.Kinds.Any(kind =>
        {
            var directory = paths.KindDirectory(kind);
            return Directory.Exists(directory)
                && Directory.EnumerateFileSystemEntries(directory).Any();
        });

    /// <summary>Every item already present under this repository's <c>.ai/</c> tree.</summary>
    public Dictionary<string, List<string>> LocalSelection()
    {
        var selection = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var kind in ResolverDefaults.Kinds)
        {
            var baseDirectory = paths.KindDirectory(kind);
            var names = new List<string>();
            if (Directory.Exists(baseDirectory))
            {
                if (kind is "agent" or "instruction" or "prompt")
                {
                    var suffix = ResolverDefaults.KindSuffixes[kind];
                    names = Directory.EnumerateFiles(baseDirectory)
                        .Where(file => file.EndsWith(suffix, StringComparison.Ordinal))
                        .Select(file => Path.GetFileName(file)[..^suffix.Length])
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList();
                }
                else
                {
                    names = Directory.EnumerateDirectories(baseDirectory)
                        .Select(Path.GetFileName)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList()!;
                }
            }

            selection[kind] = names;
        }

        return selection;
    }

    public CategoryVocabulary Categories => categories ??= LoadVocabularyFile("categories.json");

    public PresetCatalog Presets => presets ??= LoadPresetFile();

    public CatalogIndex BuildIndex(
        ManifestDocument manifest,
        bool refresh = false,
        IReadOnlySet<string>? onlySources = null)
    {
        var catalogSources = sources.ToCatalogSources(manifest, refresh, onlySources);
        var started = Stopwatch.GetTimestamp();
        Console.Error.WriteLine(
            $"building catalog index from {catalogSources.Count} source(s) (refresh={refresh}) ...");
        var index = new CatalogIndexBuilder(catalogSources, Categories, Console.Error.WriteLine).Build();
        Console.Error.WriteLine(
            $"catalog index: {index.Items.Count} item(s) in "
            + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        return index;
    }

    public SelectionEngine Engine(CatalogIndex index) => new(index.ByKey(), Presets);

    public LockDocument LoadLock() => LockDocument.Load(paths.LockPath);

    public static string MaterializeMode(ManifestDocument manifest, string? overrideMode = null)
    {
        var mode = overrideMode ?? manifest.GetString("materialize") ?? "link";
        if (mode is not ("link" or "copy"))
        {
            throw new ResolverException($"materialize must be 'link' or 'copy', not '{mode}'");
        }

        return mode;
    }

    public BrowsePayload Browse(bool refresh = false)
    {
        var manifest = ManifestOrStarter();
        var index = BuildIndex(manifest, refresh);
        var indexByKey = index.ByKey();
        var engine = Engine(index);
        var selected = engine.EffectiveSelection(manifest, strict: false);
        var selectedKeys = selected.ToHashSet();
        var declaredSources = SourceCatalog.Sources(manifest);
        var roots = declaredSources.ToDictionary(
            source => source.Name,
            source => sources.SourceRoot(source.Name, source.Entry),
            StringComparer.Ordinal);
        var lockDocument = LoadLock();
        var locked = lockDocument.ByKey();
        var frozen = engine.LockedIds(manifest);
        var mode = MaterializeMode(manifest);
        var templateRoot = roots.GetValueOrDefault(ResolverDefaults.TemplateSource, paths.Root);

        var requiredBy = new Dictionary<(string Kind, string Id), List<string>>();
        foreach (var item in index.Items)
        {
            foreach (var dependency in item.Requires)
            {
                var key = ("skill", dependency);
                if (!requiredBy.TryGetValue(key, out var who))
                {
                    who = [];
                    requiredBy[key] = who;
                }

                who.Add(item.Id);
            }
        }

        var summary = new MutableSummary { Available = index.Items.Count, Selected = selected.Count };
        var items = new List<BrowseItem>();
        foreach (var item in index.Items)
        {
            var key = (item.Kind, item.Id);
            var sourceRoot = roots.GetValueOrDefault(item.Source, paths.Root);
            var sameDirectory = WorkspacePaths.SamePath(sourceRoot, paths.Root);
            var target = paths.TargetPath(item.Kind, item.Name);
            var entry = locked.GetValueOrDefault(key);
            var isSelected = selectedKeys.Contains(key);
            var isRecorded = entry is not null;

            // A lock entry is physical installed state even when the manifest no longer selects
            // it (for example after a selection was cleared or an item was deselected): show it
            // as installed instead of pretending it is an untouched catalog row.
            var state = isSelected || isRecorded
                ? StatusEvaluator.Evaluate(item.Kind, item, target, entry, sameDirectory)
                : ItemStatus.Available;
            if (isSelected && frozen.Contains(item.Id) && state is
                ItemStatus.UpstreamChanged or ItemStatus.LocalModified or ItemStatus.Both)
            {
                state = ItemStatus.Locked;
            }

            var exists = PathOps.ExistsOrLink(target);
            var localModified = entry is not null
                && exists
                && !string.Equals(ContentHash.InstalledHash(item, target), entry.Sha256, StringComparison.Ordinal);
            var moved = entry is not null && StatusEvaluator.UpstreamChanged(item, entry);

            if (state is ItemStatus.Ok or ItemStatus.Locked)
            {
                summary.Installed++;
            }
            else if (state != ItemStatus.Available)
            {
                summary.Drift++;
            }

            if (state == ItemStatus.UpstreamChanged)
            {
                summary.Updates++;
            }

            if (state == ItemStatus.Both)
            {
                summary.Conflicts++;
            }

            items.Add(new BrowseItem(
                item.Id,
                ItemId.Display(item.Id),
                item.Kind,
                item.Name,
                item.Path,
                item.Description,
                item.Category,
                item.Tags,
                item.Source,
                item.Ref,
                item.License,
                item.Upstream,
                item.Sha256,
                item.Files,
                item.Requires,
                requiredBy.GetValueOrDefault(key)?.OrderBy(id => id, StringComparer.Ordinal).ToList() ?? [],
                isSelected,
                state,
                exists && (isSelected || isRecorded),
                frozen.Contains(item.Id),
                localModified,
                moved));
        }

        var sourcePayload = new BrowseSource(
            ResolverDefaults.TemplateSource,
            templateRoot,
            SourceCatalog.SourceRef(manifest),
            WorkspacePaths.SamePath(templateRoot, paths.Root),
            declaredSources.Any(source => SourceCatalog.IsRemote(source.Entry)));

        return new BrowsePayload(
            sourcePayload,
            mode,
            ResolverDefaults.Clients.Where(client => SelectionEngine.Clients(manifest).Contains(client)).ToList(),
            new BrowseSummary(
                summary.Available,
                summary.Selected,
                summary.Installed,
                summary.Drift,
                summary.Updates,
                summary.Conflicts),
            SourceCatalog.Rows(manifest),
            PresetCards(indexByKey, engine),
            CatalogPayloads.CategoryRows(index.Items, Categories),
            items);
    }

    public PresetsPayload PresetPayload()
    {
        var manifest = ManifestOrStarter();
        var index = BuildIndex(manifest);
        var engine = Engine(index);
        var cards = PresetCards(index.ByKey(), engine);
        var active = SelectionEngine.PresetNames(manifest);
        return new PresetsPayload(active.Count > 0 ? active[0] : null, active, cards);
    }

    public UpdatePlan PlanUpdates(IReadOnlyCollection<string>? ids = null, bool force = false)
    {
        Console.Error.WriteLine("checking upstream ...");
        var started = Stopwatch.GetTimestamp();
        var manifest = ManifestOrStarter();
        // First read the cached index to find what is selected; then refresh only the sources
        // those selected items come from. Refreshing every declared source made the Updates tab
        // re-download large unselected catalogs.
        var cachedIndex = BuildIndex(manifest);
        var cachedByKey = cachedIndex.ByKey();
        var selected = Engine(cachedIndex).EffectiveSelection(manifest, strict: false);
        var lockDocument = LoadLock();
        var lockByKey = lockDocument.ByKey();
        var selectedKeys = selected.ToHashSet();
        // A lock entry is installed even when the manifest no longer selects it; include it in
        // the update check instead of showing an Updates badge with an empty overview.
        foreach (var key in lockByKey.Keys)
        {
            if (selectedKeys.Add(key))
            {
                selected.Add(key);
            }
        }

        var selectedSources = selected
            .Select(entry => cachedByKey.TryGetValue(entry, out var item) ? item.Source : ResolverDefaults.TemplateSource)
            .ToHashSet(StringComparer.Ordinal);
        Console.Error.WriteLine(
            $"refreshing {selectedSources.Count} selected source(s): "
            + string.Join(", ", selectedSources.OrderBy(name => name, StringComparer.Ordinal)));
        var index = BuildIndex(manifest, refresh: true, onlySources: selectedSources);
        var indexByKey = index.ByKey();
        var engine = Engine(index);
        var declaredSources = SourceCatalog.Sources(manifest).ToDictionary(source => source.Name, StringComparer.Ordinal);

        engine.CheckHookGate(manifest, selected, sources, paths);

        var remoteSelected = selected.Any(entry =>
        {
            var sourceName = indexByKey.TryGetValue(entry, out var item)
                ? item.Source
                : ResolverDefaults.TemplateSource;
            return declaredSources.TryGetValue(sourceName, out var sourceEntry)
                && SourceCatalog.IsRemote(sourceEntry.Entry);
        });

        var locked = lockByKey;
        var mode = MaterializeMode(manifest);
        var wanted = ids is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        var frozen = engine.LockedIds(manifest);

        var rows = new List<UpdateRow>();
        foreach (var (kind, itemId) in selected)
        {
            if (wanted.Count > 0 && !wanted.Contains(itemId) && !wanted.Contains(ItemId.Display(itemId)))
            {
                continue;
            }

            indexByKey.TryGetValue((kind, itemId), out var item);
            var entry = locked.GetValueOrDefault((kind, itemId));
            var sourceName = item?.Source ?? ResolverDefaults.TemplateSource;
            var root = declaredSources.TryGetValue(sourceName, out var sourceEntry)
                ? sources.SourceRoot(sourceName, sourceEntry.Entry)
                : paths.Root;
            var target = paths.TargetPath(kind, item?.Name ?? ItemId.Display(itemId));
            var state = StatusEvaluator.Evaluate(kind, item, target, entry, WorkspacePaths.SamePath(root, paths.Root));
            rows.Add(new UpdateRow(
                itemId,
                ItemId.Display(itemId),
                kind,
                state,
                frozen.Contains(itemId),
                item?.Path ?? entry?.Path ?? string.Empty,
                sourceName));
        }

        var updatable = new[] { ItemStatus.UpstreamChanged, ItemStatus.NotInstalled, ItemStatus.Missing };
        var conflict = new[] { ItemStatus.LocalModified, ItemStatus.Both };
        var ready = rows.Where(row => !row.Locked && updatable.Contains(row.Status))
            .Concat(rows.Where(row => row.Locked && row.Status is ItemStatus.NotInstalled or ItemStatus.Missing))
            .ToList();
        var blocked = rows.Where(row => !row.Locked && conflict.Contains(row.Status)).ToList();
        var held = rows.Where(row => row.Locked && row.Status is not (ItemStatus.NotInstalled or ItemStatus.Missing))
            .ToList();
        var chosen = ready.Concat(force ? blocked : []).ToList();

        var plan = new UpdatePlan(
            manifest,
            index,
            indexByKey,
            selected,
            lockDocument,
            mode,
            frozen,
            rows,
            ready,
            blocked,
            held,
            chosen,
            remoteSelected,
            declaredSources.Values.Any(source => SourceCatalog.IsRemote(source.Entry)));
        Console.Error.WriteLine(
            $"update plan: {ready.Count} ready, {blocked.Count} blocked, {held.Count} locked in "
            + $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        return plan;
    }

    public UpdatePayload Updates(IReadOnlyCollection<string>? ids = null, bool force = false)
    {
        var plan = PlanUpdates(ids, force);
        return new UpdatePayload(true, plan.Ready, plan.Blocked, plan.Held, plan.Remote, plan.RemoteSelected);
    }

    public StatusPayload Status()
    {
        var manifest = ManifestOrStarter();
        var index = BuildIndex(manifest);
        var indexByKey = index.ByKey();
        var engine = Engine(index);
        var templateRoot = sources.TemplateRoot(manifest);
        var sameDirectory = WorkspacePaths.SamePath(templateRoot, paths.Root);
        var lockDocument = LoadLock();
        var locked = lockDocument.ByKey();
        var selected = engine.EffectiveSelection(manifest, strict: false);
        var mode = MaterializeMode(manifest);

        var rows = new List<StatusItem>();
        foreach (var (kind, itemId) in selected)
        {
            indexByKey.TryGetValue((kind, itemId), out var item);
            var name = item?.Name ?? ItemId.Display(itemId);
            var target = paths.TargetPath(kind, name);
            var entry = locked.GetValueOrDefault((kind, itemId));
            var status = StatusEvaluator.Evaluate(kind, item, target, entry, sameDirectory);
            rows.Add(new StatusItem(
                itemId,
                kind,
                entry?.Path ?? item?.Path ?? string.Empty,
                status,
                ItemStatus.UpstreamUnchanged,
                true,
                entry?.Materialize ?? mode));
        }

        var selectedKeys = selected.ToHashSet();
        foreach (var entry in lockDocument.Entries
            .Where(entry => !selectedKeys.Contains((entry.Kind, entry.Id)))
            .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal))
        {
            var target = Path.Combine(paths.Root, entry.Path);
            var exists = PathOps.ExistsOrLink(target);
            rows.Add(new StatusItem(
                entry.Id,
                entry.Kind,
                entry.Path,
                exists ? ItemStatus.RemovedUpstream : ItemStatus.Missing,
                ItemStatus.UpstreamUnchanged,
                false,
                entry.Materialize ?? mode));
        }

        return new StatusPayload(
            new StatusSource(
                ResolverDefaults.TemplateSource,
                templateRoot,
                SourceCatalog.SourceRef(manifest),
                sameDirectory),
            rows);
    }

    public ListPayload List(bool installedOnly = false, bool availableOnly = false)
    {
        var manifest = ManifestOrStarter();
        var index = BuildIndex(manifest);
        var engine = Engine(index);
        var lockDocument = File.Exists(paths.LockPath) ? LoadLock() : LockDocument.Empty;
        var selected = engine.EffectiveSelection(manifest, strict: false).ToHashSet();
        var lockedKeys = lockDocument.Entries.Select(entry => (entry.Kind, entry.Id)).ToHashSet();
        var installedKeys = selected.Concat(lockedKeys).ToHashSet();

        var onlyInstalled = installedOnly && !availableOnly;
        var showInstalled = installedOnly || !availableOnly;
        var showAvailable = availableOnly || !installedOnly;

        var rows = new List<ListItem>();
        foreach (var item in index.Items)
        {
            var key = (item.Kind, item.Id);
            var isInstalled = installedKeys.Contains(key);
            if (onlyInstalled && !isInstalled)
            {
                continue;
            }

            rows.Add(new ListItem(
                item.Id,
                item.Kind,
                item.Name,
                item.Path,
                item.Description,
                item.Sha256,
                item.Ref,
                isInstalled,
                isInstalled ? (selected.Contains(key) ? "selected" : "locked") : string.Empty));
        }

        var catalogKeys = index.Items.Select(item => (item.Kind, item.Id)).ToHashSet();
        if (showInstalled)
        {
            foreach (var entry in lockDocument.Entries.Where(entry => !catalogKeys.Contains((entry.Kind, entry.Id))))
            {
                rows.Add(new ListItem(
                    entry.Id,
                    entry.Kind,
                    ItemId.Display(entry.Id),
                    entry.Path,
                    "(not in the catalog)",
                    entry.Sha256 ?? string.Empty,
                    entry.Ref ?? string.Empty,
                    true,
                    "locked"));
            }
        }

        var available = rows
            .Where(row => row.State != "locked" || catalogKeys.Contains((row.Kind, row.Id)))
            .Select(row => row.Id)
            .ToList();
        var installed = rows.Where(row => row.Installed).Select(row => row.Id).ToList();
        var filtered = showAvailable && !showInstalled
            ? rows.Where(row => !row.Installed || catalogKeys.Contains((row.Kind, row.Id))).ToList()
            : rows;
        return new ListPayload(filtered, available, installed);
    }

    public ReleasePayload Release()
    {
        var manifest = ManifestOrStarter();
        var entries = SourceCatalog.Sources(manifest)
            .Where(source => source.Entry["path"] is null)
            .ToList();
        var rows = new ReleaseRow[entries.Count];
        // Tag reads are independent network calls. Sequentially, one unreachable source added
        // its full timeout before the next one started; the Updates tab paid that five times.
        Parallel.For(0, entries.Count, index =>
        {
            rows[index] = ReleaseRow(entries[index].Name, entries[index].Entry);
        });
        return new ReleasePayload(Offline, rows.Where(row => row.Newer).Select(row => row.Id).ToList(), rows);
    }

    public ReleaseRow ReleaseRow(string name, JsonObject entry)
    {
        var repo = StringOf(entry, "repo") ?? string.Empty;
        var pinned = StringOf(entry, "ref") ?? ResolverDefaults.LocalRef;
        var current = SemanticVersion.TryParse(pinned);
        var (tags, error) = sources.RemoteTags(repo);
        var releases = SemanticVersion.ReleaseTags(tags ?? []);
        var latest = releases.Count > 0 ? releases[0] : null;
        var stable = releases.FirstOrDefault(tag => SemanticVersion.TryParse(tag)?.IsStable == true);
        latest = stable ?? latest;
        var newer = current is not null
            && latest is not null
            && SemanticVersion.TryParse(latest) > current;
        return new ReleaseRow(
            name,
            repo,
            pinned,
            current is not null,
            current is not null ? pinned : null,
            latest,
            latest is not null && SemanticVersion.TryParse(latest)?.IsStable == false,
            newer,
            releases.Take(20).ToList(),
            releases.Count,
            error is null,
            error);
    }

    private List<PresetCard> PresetCards(
        IReadOnlyDictionary<(string Kind, string Id), CatalogItem> indexByKey,
        SelectionEngine engine)
    {
        var cards = new List<PresetCard>();
        foreach (var name in Presets.Order)
        {
            var body = Presets.Presets[name];
            var declared = engine.PresetSelection([name], strict: false);
            List<(string Kind, string Id)> resolved;
            try
            {
                resolved = engine.Closure(engine.PresetSelected(name, strict: false));
            }
            catch (ResolverException)
            {
                resolved = [];
            }

            if (resolved.Count > 0)
            {
                cards.Add(new PresetCard(
                    name,
                    body.Description,
                    resolved.Count(entry => entry.Kind == "skill"),
                    resolved.Count(entry => entry.Kind == "agent"),
                    resolved.Select(entry => entry.Id).ToList(),
                    body.Recommended));
                continue;
            }

            var ids = ResolverDefaults.Kinds
                .SelectMany(kind => (declared.GetValueOrDefault(kind) ?? []).Select(id => (Kind: kind, Id: id)))
                .ToList();
            cards.Add(new PresetCard(
                name,
                body.Description,
                declared.GetValueOrDefault("skill")?.Count ?? 0,
                declared.GetValueOrDefault("agent")?.Count ?? 0,
                ids.Select(entry => entry.Id).ToList(),
                body.Recommended));
        }

        return cards;
    }

    private CategoryVocabulary LoadVocabularyFile(string fileName)
    {
        foreach (var candidate in VocabularyCandidates(fileName))
        {
            if (File.Exists(candidate))
            {
                return CategoryVocabulary.Load(candidate);
            }
        }

        return CategoryVocabulary.Empty;
    }

    private PresetCatalog LoadPresetFile()
    {
        foreach (var candidate in VocabularyCandidates("presets.json"))
        {
            if (File.Exists(candidate))
            {
                return PresetCatalog.Load(candidate);
            }
        }

        return PresetCatalog.Empty;
    }

    private IEnumerable<string> VocabularyCandidates(string fileName)
    {
        yield return Path.Combine(paths.AiDirectory, "catalog", fileName);
        yield return Path.Combine(paths.Root, "catalog", fileName);
        if (!string.IsNullOrEmpty(BundledToolingRoot))
        {
            yield return Path.Combine(BundledToolingRoot, "catalog", fileName);
        }
    }

    private static string? StringOf(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private sealed class MutableSummary
    {
        public int Available { get; set; }

        public int Selected { get; set; }

        public int Installed { get; set; }

        public int Drift { get; set; }

        public int Updates { get; set; }

        public int Conflicts { get; set; }
    }
}
