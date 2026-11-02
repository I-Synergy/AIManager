using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Mutating commands: add/remove, locks, sync, update, pin and preset edits. The Python resolver
/// called these <c>cmd_add</c>, <c>cmd_remove</c>, <c>cmd_sync</c>, <c>cmd_update</c> and
/// <c>cmd_pin</c>; the rules (lock safety, prune boundary, orphan refusal) are preserved.
/// </summary>
public sealed class WorkspaceOperations
{
    private readonly WorkspaceQueries queries;
    private readonly WorkspaceSetupService setup;

    public WorkspaceOperations(WorkspaceQueries queries, WorkspaceSetupService? setup = null)
    {
        this.queries = queries;
        this.setup = setup ?? new WorkspaceSetupService(queries);
    }

    public WorkspaceQueries Queries => queries;

    public OperationOutcome Add(IReadOnlyList<string> ids, string? kindFlag = null)
    {
        var existing = queries.LoadManifest();
        var manifest = existing ?? queries.StarterManifest().Bind(queries.Paths.ManifestPath);
        var index = queries.BuildIndex(manifest);
        var indexByKey = index.ByKey();

        var (resolved, problems) = ResolveAddTargets(ids, kindFlag, indexByKey);
        if (problems.Count > 0)
        {
            return new OperationOutcome(
                new CliResult(2, string.Empty, string.Join("\n", problems.Select(problem => $"ERROR: {problem}"))),
                null);
        }

        var added = new List<ItemRef>();
        foreach (var (kind, itemId) in resolved)
        {
            var key = ResolverDefaults.ManifestKeys[kind];
            var selection = manifest.EnsureArray(key);
            var strings = selection.Select(ToStringOrNull).Where(value => value is not null).Select(value => value!).ToList();
            var bare = ItemId.Display(itemId);
            if (!strings.Contains(bare) && !strings.Contains(itemId))
            {
                selection.Add(bare);
                added.Add(new ItemRef(itemId, kind));
            }

            var excludes = manifest.EnsureArray("exclude");
            for (var position = excludes.Count - 1; position >= 0; position--)
            {
                if (ItemId.SoftCanonical(ToStringOrNull(excludes[position])) == itemId)
                {
                    excludes.RemoveAt(position);
                }
            }
        }

        if (added.Count > 0 || existing is null)
        {
            manifest.Save();
        }

        var payload = new AddPayload(added, queries.Paths.ManifestPath);
        var lines = new List<string>();
        if (added.Count > 0)
        {
            lines.Add($"added {added.Count} item(s) to {queries.Paths.ManifestPath}:");
            lines.AddRange(added.Select(entry => $"  + {entry.Kind,-6} {ItemId.Display(entry.Id)}"));
            lines.Add("run 'catalog.py sync' to materialise them");
        }
        else
        {
            lines.Add("nothing to add — already selected");
        }

        return new OperationOutcome(new CliResult(0, JoinLines(lines), string.Empty), payload);
    }

    public OperationOutcome Remove(
        IReadOnlyList<string> ids,
        string? kindFlag = null,
        bool prune = false,
        bool force = false)
    {
        var manifest = queries.LoadManifest()
            ?? throw new ResolverException(
                $"no manifest at {queries.Paths.ManifestPath} — nothing to remove");

        var removed = new List<RemovedItem>();
        foreach (var raw in ids)
        {
            var itemId = ItemId.Canonical(raw);
            foreach (var kind in ResolverDefaults.Kinds)
            {
                if (!string.IsNullOrEmpty(kindFlag) && kind != kindFlag)
                {
                    continue;
                }

                var key = ResolverDefaults.ManifestKeys[kind];
                var selection = manifest.GetStringArray(key).ToList();
                var kept = selection.Where(value => ItemId.SoftCanonical(value) != itemId).ToList();
                if (kept.Count != selection.Count)
                {
                    SetStringArray(manifest, key, kept);
                    removed.Add(new RemovedItem(itemId, kind, "list"));
                }
            }
        }

        var index = queries.BuildIndex(manifest);
        var indexByKey = index.ByKey();
        var engine = queries.Engine(index);
        var declared = new HashSet<(string Kind, string Id)>();
        foreach (var (kind, presetIds) in engine.PresetSelection(SelectionEngine.PresetNames(manifest), strict: false))
        {
            foreach (var itemId in presetIds)
            {
                declared.Add((kind, itemId));
            }
        }

        var selected = engine.Selection(manifest, strict: false);
        foreach (var raw in ids)
        {
            var itemId = ItemId.Canonical(raw);
            var match = selected.FirstOrDefault(entry =>
                entry.Id == itemId && declared.Contains((entry.Kind, entry.Id)));
            if (match == default || (!string.IsNullOrEmpty(kindFlag) && match.Kind != kindFlag))
            {
                continue;
            }

            var excludes = manifest.EnsureArray("exclude");
            var present = excludes
                .Select(ToStringOrNull)
                .Any(value => ItemId.SoftCanonical(value) == itemId);
            if (!present)
            {
                excludes.Add(ItemId.Display(itemId));
                if (!removed.Any(entry => entry.Id == itemId))
                {
                    removed.Add(new RemovedItem(itemId, match.Kind, "exclude"));
                }
            }
        }

        if (removed.Count == 0)
        {
            var installed = engine.Closure(engine.Selection(manifest, strict: false))
                .Select(entry => entry.Id)
                .ToHashSet(StringComparer.Ordinal);
            var requested = ids.Select(ItemId.Canonical).ToList();
            var pulled = requested.Where(installed.Contains).ToList();
            if (pulled.Count > 0 && !force)
            {
                var names = string.Join(", ", pulled.Select(id => $"'{ItemId.Display(id)}'"));
                throw new ResolverException(
                    $"{names} is not declared in ai.json — it is installed because something else "
                    + "requires it. Remove the dependent instead.");
            }
        }

        if (removed.Count > 0 && !force)
        {
            var remaining = engine.Closure(engine.Selection(manifest, strict: false));
            var requiredBy = new Dictionary<(string Kind, string Id), List<string>>();
            foreach (var (kind, itemId) in remaining)
            {
                if (!indexByKey.TryGetValue((kind, itemId), out var item))
                {
                    continue;
                }

                foreach (var dependency in item.Requires)
                {
                    var key = ("skill", dependency);
                    if (!requiredBy.TryGetValue(key, out var who))
                    {
                        who = [];
                        requiredBy[key] = who;
                    }

                    who.Add(itemId);
                }
            }

            var blocked = new List<(RemovedItem Entry, List<string> Who)>();
            foreach (var entry in removed)
            {
                var key = (entry.Kind, entry.Id);
                var who = requiredBy.GetValueOrDefault(key) ?? [];
                if (remaining.Contains(key) || who.Count > 0)
                {
                    blocked.Add((entry, who));
                }
            }

            if (blocked.Count > 0)
            {
                var detail = string.Join(
                    "; ",
                    blocked.Select(pair =>
                        $"{ItemId.Display(pair.Entry.Id)} ({pair.Entry.Kind}) is required by "
                        + $"{string.Join(", ", pair.Who.OrderBy(name => name, StringComparer.Ordinal).Select(ItemId.Display).DefaultIfEmpty("another selected item"))}"));
                var forced = blocked.Any(pair => pair.Entry.Via == "exclude")
                    ? "the dependent will be installed without it"
                    : "a later sync will restore it";
                throw new ResolverException(
                    $"refusing to orphan — {detail}. Remove the dependent first, "
                    + $"--force to edit the manifest anyway ({forced})");
            }
        }

        if (removed.Count > 0)
        {
            var removedIds = removed.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
            var locks = manifest.GetStringArray("locked")
                .Where(value => !removedIds.Contains(ItemId.SoftCanonical(value) ?? string.Empty))
                .ToList();
            SetStringArray(manifest, "locked", locks);
            manifest.Save();
        }

        var pruned = new List<PrunedItem>();
        if (prune)
        {
            var lockDocument = queries.LoadLock();
            var drop = removed.Select(entry => (entry.Kind, entry.Id)).ToHashSet();
            var remaining = new List<LockEntry>();
            foreach (var entry in lockDocument.Entries)
            {
                if (!drop.Contains((entry.Kind, entry.Id)))
                {
                    remaining.Add(entry);
                    continue;
                }

                var (deleted, markers) = Materializer.PruneEntry(queries.Paths, entry);
                pruned.Add(new PrunedItem(entry.Id, entry.Kind, entry.Path, deleted is not null, markers));
            }

            if (remaining.Count != lockDocument.Entries.Count)
            {
                LockDocument.Save(queries.Paths.LockPath, remaining);
            }
        }

        var payload = new RemovePayload(removed, pruned);
        if (removed.Count == 0)
        {
            return new OperationOutcome(
                new CliResult(0, "nothing to remove — no such selection in the manifest", string.Empty),
                payload);
        }

        var output = new List<string>();
        output.AddRange(removed.Select(entry => $"  - {entry.Kind,-6} {ItemId.Display(entry.Id)}"));
        if (prune)
        {
            if (pruned.Count == 0)
            {
                output.Add("  nothing lock-recorded to prune");
            }
            else
            {
                output.AddRange(pruned.Select(entry =>
                    $"  pruned {entry.Path} ({(entry.Deleted ? "deleted" : "not on disk")})"));
            }
        }
        else
        {
            output.Add("installed files were left alone: add --prune to delete lock-recorded paths");
        }

        return new OperationOutcome(new CliResult(0, JoinLines(output), string.Empty), payload);
    }

    public OperationOutcome SetLocked(IReadOnlyList<string> ids, bool locked)
    {
        var manifest = queries.LoadManifest()
            ?? throw new ResolverException(
                $"no manifest at {queries.Paths.ManifestPath} — nothing to lock");
        var index = queries.BuildIndex(manifest);
        var engine = queries.Engine(index);
        var selected = engine.EffectiveSelection(manifest, strict: false)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        var entries = manifest.GetStringArray("locked").ToList();
        var current = entries.Select(value => ItemId.SoftCanonical(value) ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var changed = new List<string>();

        foreach (var raw in ids)
        {
            var itemId = ItemId.Canonical(raw);
            if (locked)
            {
                if (!selected.Contains(itemId))
                {
                    throw new ResolverException(
                        $"'{ItemId.Display(itemId)}' is not selected — lock an item this project installs");
                }

                if (current.Contains(itemId))
                {
                    continue;
                }

                entries.Add(ItemId.Display(itemId));
                current.Add(itemId);
                changed.Add(itemId);
            }
            else
            {
                var kept = entries.Where(value => ItemId.SoftCanonical(value) != itemId).ToList();
                if (kept.Count != entries.Count)
                {
                    entries = kept;
                    current.Remove(itemId);
                    changed.Add(itemId);
                }
            }
        }

        SetStringArray(manifest, "locked", entries);
        manifest.Save();

        var lockedNames = entries
            .Select(value => ItemId.Display(ItemId.SoftCanonical(value)))
            .ToList();
        var payload = new LockPayload(
            locked ? "lock" : "unlock",
            lockedNames,
            changed.Select(ItemId.Display).ToList());
        var lines = new List<string>();
        if (changed.Count > 0)
        {
            lines.Add($"{(locked ? "locked" : "unlocked")}: {string.Join(", ", changed.Select(ItemId.Display))}");
        }
        else
        {
            lines.Add($"nothing to {(locked ? "lock" : "unlock")} — {(locked ? "already locked" : "not locked")}");
        }

        lines.Add(locked
            ? "locked items are never overwritten by update or sync; unlock to resume updates"
            : "unlocked items follow the catalog again on the next update");
        return new OperationOutcome(new CliResult(0, JoinLines(lines), string.Empty), payload);
    }

    public SyncOutcome Sync(
        bool check = false,
        bool frozen = false,
        bool prune = false,
        bool dryRun = false,
        string? materializeOverride = null)
    {
        var existing = queries.LoadManifest();
        var fresh = existing is null;
        var manifest = existing ?? queries.StarterManifest();
        var mode = WorkspaceQueries.MaterializeMode(manifest, materializeOverride);
        var templateRoot = queries.Sources.TemplateRoot(manifest);
        var sameDirectory = WorkspacePaths.SamePath(templateRoot, queries.Paths.Root);
        var index = queries.BuildIndex(manifest);
        var indexByKey = index.ByKey();
        var engine = queries.Engine(index);
        var selected = engine.EffectiveSelection(manifest, strict: true);
        engine.CheckHookGate(manifest, selected, queries.Sources, queries.Paths);

        var lockDocument = queries.LoadLock();
        var locked = lockDocument.ByKey();
        var frozenIds = engine.LockedIds(manifest);
        var reference = SourceCatalog.SourceRef(manifest);

        var actions = new List<SyncAction>();
        var conflicts = new List<string>();
        var entries = new List<LockEntry>();
        var targets = new List<string>();

        foreach (var (kind, itemId) in selected)
        {
            if (!indexByKey.TryGetValue((kind, itemId), out var item))
            {
                continue;
            }

            var target = queries.Paths.TargetPath(kind, item.Name);
            targets.Add(target);
            var itemSource = item.Source;
            var itemRoot = queries.Sources.SourceRoot(itemSource, SourceCatalog.EntryNamed(manifest, itemSource));
            var source = Path.Combine(itemRoot, item.SourcePath ?? item.Path);
            var entry = LockEntry.Make(kind, item, mode);
            var previous = locked.GetValueOrDefault((kind, itemId));

            if (WorkspacePaths.SamePath(itemRoot, queries.Paths.Root))
            {
                entries.Add(entry);
                actions.Add(Action("no-op", kind, itemId, item.Path, "source == target"));
                continue;
            }

            var exists = PathOps.ExistsOrLink(target);
            if (!exists)
            {
                entries.Add(entry);
                actions.Add(Action("create", kind, itemId, item.Path));
            }
            else if (previous is null)
            {
                actions.Add(Action("conflict", kind, itemId, item.Path, "untracked path exists"));
                conflicts.Add($"{item.Path} exists but is not recorded in the lock");
            }
            else if (frozenIds.Contains(itemId))
            {
                entries.Add(previous);
                actions.Add(Action("ok", kind, itemId, item.Path, "locked"));
            }
            else if (WorkspacePaths.SamePath(source, target))
            {
                entries.Add(entry);
                actions.Add(Action("ok", kind, itemId, item.Path));
            }
            else
            {
                entries.Add(entry);
                var current = ContentHash.InstalledHash(item, target);
                var needs = !string.Equals(current, entry.Sha256, StringComparison.Ordinal)
                    || !string.Equals(previous.Path, entry.Path, StringComparison.Ordinal)
                    || !string.Equals(previous.Materialize ?? "link", mode, StringComparison.Ordinal);
                actions.Add(Action(needs ? "update" : "ok", kind, itemId, item.Path));
            }
        }

        var markerFiles = sameDirectory ? [] : Materializer.LegacyMarkers(queries.Paths, targets);
        var oldIndex = sameDirectory ? null : Materializer.LegacyIndex(queries.Paths);

        var selectedKeys = selected.ToHashSet();
        var knownKinds = ResolverDefaults.Kinds.ToHashSet(StringComparer.Ordinal);
        var pendingPrune = new List<LockEntry>();
        var preservedUnknown = new List<LockEntry>();
        foreach (var (_, entry) in locked)
        {
            if (selectedKeys.Contains((entry.Kind, entry.Id)))
            {
                continue;
            }

            if (knownKinds.Contains(entry.Kind))
            {
                pendingPrune.Add(entry);
            }
            else
            {
                preservedUnknown.Add(entry);
            }
        }

        pendingPrune.Sort((left, right) =>
        {
            var compared = string.CompareOrdinal(left.Kind, right.Kind);
            return compared != 0 ? compared : string.CompareOrdinal(left.Id, right.Id);
        });

        if (!prune)
        {
            entries.AddRange(pendingPrune);
        }

        entries.AddRange(preservedUnknown);

        var newLock = LockDocument.FromEntries(entries);
        var lockChanged = !sameDirectory
            && !string.Equals(LockDocument.Signature(newLock), LockDocument.Signature(lockDocument), StringComparison.Ordinal);
        var changes = actions.Where(action => action.Action is "create" or "update" or "conflict").ToList();
        var drift = changes.Count > 0
            || pendingPrune.Count > 0
            || lockChanged
            || markerFiles.Count > 0
            || oldIndex is not null;

        if (check)
        {
            var report = new SyncReport(
                "check",
                !drift,
                sameDirectory,
                actions,
                pendingPrune.Select(entry => entry.Path).ToList(),
                preservedUnknown.Select(entry => entry.Id).ToList(),
                markerFiles.Select(path => Relative(path)).ToList(),
                oldIndex is null ? null : Relative(oldIndex),
                lockChanged,
                [],
                0,
                false,
                false,
                conflicts);
            var lines = new List<string>();
            if (sameDirectory)
            {
                lines.Add("no-op: source == target");
            }

            lines.AddRange(Describe(actions, pendingPrune));
            if (drift)
            {
                lines.Add("drift detected");
            }
            else
            {
                lines.Add("in sync");
            }

            return new SyncOutcome(new CliResult(drift ? 1 : 0, JoinLines(lines), string.Empty), report);
        }

        if (frozen && lockChanged)
        {
            var report = new SyncReport(
                "frozen",
                false,
                sameDirectory,
                actions,
                pendingPrune.Select(entry => entry.Path).ToList(),
                preservedUnknown.Select(entry => entry.Id).ToList(),
                [],
                null,
                true,
                [],
                0,
                false,
                false,
                conflicts);
            return new SyncOutcome(
                new CliResult(1, JoinLines(Describe(actions, pendingPrune).Append($"frozen lock would need to change: {queries.Paths.LockPath}")), string.Empty),
                report);
        }

        var performed = new List<SyncPerformed>();
        var markersRemoved = 0;
        var legacyIndexRemoved = false;
        if (!dryRun)
        {
            foreach (var marker in markerFiles)
            {
                try
                {
                    File.Delete(marker);
                    markersRemoved++;
                }
                catch (IOException)
                {
                    // already gone with a directory replaced by materialize
                }
            }

            if (oldIndex is not null)
            {
                try
                {
                    File.Delete(oldIndex);
                    legacyIndexRemoved = true;
                }
                catch (IOException)
                {
                    // best effort
                }
            }

            if (markerFiles.Count > 0
                && Directory.Exists(queries.Paths.InstalledDirectory)
                && !Directory.EnumerateFileSystemEntries(queries.Paths.InstalledDirectory).Any())
            {
                Directory.Delete(queries.Paths.InstalledDirectory);
            }

            foreach (var action in actions.Where(action => action.Action is "create" or "update"))
            {
                if (!indexByKey.TryGetValue((action.Kind, action.Id), out var item))
                {
                    continue;
                }

                var target = queries.Paths.TargetPath(action.Kind, item.Name);
                var itemRoot = queries.Sources.SourceRoot(
                    item.Source,
                    SourceCatalog.EntryNamed(manifest, item.Source));
                var source = Path.Combine(itemRoot, item.SourcePath ?? item.Path);
                var ok = Materializer.MaterializeItem(item, source, target, mode);
                performed.Add(new SyncPerformed(action.Action, action.Kind, action.Id, action.Path, ok));
                if (!ok)
                {
                    conflicts.Add($"could not materialize {action.Path}");
                }
            }

            if (prune)
            {
                foreach (var entry in pendingPrune)
                {
                    var (deleted, markers) = Materializer.PruneEntry(queries.Paths, entry);
                    performed.Add(new SyncPerformed(
                        "delete",
                        entry.Kind,
                        entry.Id,
                        entry.Path,
                        true,
                        deleted is not null,
                        markers));
                }
            }

            if (lockChanged)
            {
                LockDocument.Save(queries.Paths.LockPath, entries);
            }

            _ = setup.InstallTooling(templateRoot, overwrite: false, reference);
            _ = setup.InstallAgentConfig(manifest.Root);
            if (fresh)
            {
                _ = setup.InstallScaffold(templateRoot);
            }

            _ = setup.EnsureGitignore();
            _ = new PlatformMirrorService(queries.Paths).Sync(manifest);
        }

        if (!dryRun)
        {
            _ = new InstructionDocumentService(queries).Update(manifest.Root);
        }

        var applied = new SyncReport(
            dryRun ? "dry-run" : "apply",
            false,
            sameDirectory,
            actions,
            pendingPrune.Select(entry => entry.Path).ToList(),
            preservedUnknown.Select(entry => entry.Id).ToList(),
            markerFiles.Select(Relative).ToList(),
            oldIndex is null ? null : Relative(oldIndex),
            lockChanged,
            performed,
            markersRemoved,
            legacyIndexRemoved,
            prune,
            conflicts);

        var output = new List<string>();
        var prefix = dryRun ? "[DRY RUN] " : string.Empty;
        if (sameDirectory)
        {
            output.Add("no-op: source == target");
        }

        output.AddRange(Describe(actions, pendingPrune));
        if (pendingPrune.Count > 0 && !prune)
        {
            output.Add($"{prefix}{pendingPrune.Count} item(s) pending prune (run 'catalog.py sync --prune')");
        }

        var touched = actions.Count(action => action.Action is "create" or "update");
        if (dryRun)
        {
            output.Add($"{prefix}{touched} item(s) would change; nothing written");
        }
        else
        {
            output.Add($"{prefix}{touched} item(s) changed; {(lockChanged ? "lock updated" : "lock unchanged")}");
        }

        var errors = conflicts.Select(conflict => $"CONFLICT: {conflict}").ToList();
        return new SyncOutcome(
            new CliResult(conflicts.Count > 0 ? 1 : 0, JoinLines(output), JoinLines(errors)),
            applied);
    }

    public OperationOutcome ApplyUpdates(
        IReadOnlyCollection<string>? ids = null,
        bool force = false,
        bool tooling = false)
    {
        var plan = queries.PlanUpdates(ids, force);
        var entries = plan.Lock.ByKey().ToDictionary(pair => pair.Key, pair => pair.Value);
        var applied = new List<UpdateAppliedItem>();
        foreach (var row in plan.Chosen)
        {
            if (!plan.IndexByKey.TryGetValue((row.Kind, row.Id), out var item))
            {
                continue;
            }

            var root = queries.Sources.SourceRoot(
                item.Source,
                SourceCatalog.EntryNamed(plan.Manifest, item.Source));
            var target = queries.Paths.TargetPath(row.Kind, item.Name);
            var source = Path.Combine(root, item.SourcePath ?? item.Path);
            var ok = Materializer.MaterializeItem(item, source, target, plan.Mode);
            applied.Add(new UpdateAppliedItem(row.Id, ok, row.Status));
            if (ok)
            {
                entries[(row.Kind, row.Id)] = LockEntry.Make(row.Kind, item, plan.Mode);
            }
        }

        var templateRoot = queries.Sources.TemplateRoot(plan.Manifest);
        if (!WorkspacePaths.SamePath(templateRoot, queries.Paths.Root))
        {
            LockDocument.Save(queries.Paths.LockPath, entries.Values);
        }

        if (tooling)
        {
            _ = setup.MigrateCatalogDirectory();
            var sourceRoot = queries.Sources.TemplateRoot(plan.Manifest);
            _ = setup.InstallTooling(sourceRoot, overwrite: true, SourceCatalog.SourceRef(plan.Manifest));
            _ = setup.InstallAgentConfig(plan.Manifest.Root);
            _ = setup.EnsureGitignore();
            _ = new PlatformMirrorService(queries.Paths).Sync(plan.Manifest);
        }

        _ = new InstructionDocumentService(queries).Update(plan.Manifest.Root);

        var payload = new UpdateApplyPayload(
            true,
            false,
            applied,
            plan.Blocked,
            plan.Held,
            plan.RemoteSelected);
        var lines = new List<string>();
        lines.AddRange(applied.Select(entry => $"  {(entry.Ok ? "OK" : "FAILED"),-8} {ItemId.Display(entry.Id)}"));
        lines.AddRange(plan.Held.Select(row => $"  {"LOCKED",-8} {row.DisplayId} ({row.Kind}) — left untouched"));
        lines.Add($"\n{applied.Count} item(s) updated, {plan.Held.Count} locked and left untouched.");
        return new OperationOutcome(
            new CliResult(applied.All(entry => entry.Ok) ? 0 : 1, JoinLines(lines), string.Empty),
            payload);
    }

    public OperationOutcome Pin(string? release = null, string? repo = null)
    {
        var existing = queries.LoadManifest();
        var manifest = existing ?? queries.StarterManifest().Bind(queries.Paths.ManifestPath);
        var sources = manifest.GetObject("sources") ?? new JsonObject();
        if (manifest.GetObject("sources") is null)
        {
            manifest.Set("sources", sources);
        }

        var entry = sources[ResolverDefaults.TemplateSource] is JsonObject template
            ? (JsonObject)template.DeepClone()
            : new JsonObject();
        var tag = release;
        if (string.IsNullOrWhiteSpace(tag))
        {
            var row = queries.ReleaseRow(ResolverDefaults.TemplateSource, entry);
            tag = row.Latest;
            if (string.IsNullOrWhiteSpace(tag))
            {
                throw new ResolverException(
                    $"no release to pin {ResolverDefaults.TemplateSource} to: "
                    + $"{row.Error ?? "no semantic-version tags were found"}");
            }
        }

        var index = queries.BuildIndex(manifest);
        var indexByKey = index.ByKey();
        var engine = queries.Engine(index);
        var selected = engine.EffectiveSelection(manifest, strict: false);
        var wasLocalTree = WorkspacePaths.SamePath(queries.Sources.TemplateRoot(manifest), queries.Paths.Root);
        var lists = ResolverDefaults.Kinds.ToDictionary(
            kind => kind,
            _ => new List<string>(),
            StringComparer.Ordinal);
        foreach (var (kind, itemId) in selected)
        {
            if (!indexByKey.TryGetValue((kind, itemId), out var item)
                || item.Source != ResolverDefaults.TemplateSource)
            {
                continue;
            }

            lists[kind].Add(ItemId.Display(itemId));
        }

        entry["repo"] = repo ?? (entry["repo"] is JsonValue repoValue && repoValue.TryGetValue<string>(out var repoText)
            ? repoText
            : ResolverDefaults.TemplateRepo);
        entry["ref"] = tag;
        entry["resolve"] = "remote";
        entry.Remove("path");
        sources[ResolverDefaults.TemplateSource] = entry;
        foreach (var (kind, values) in lists)
        {
            SetStringArray(manifest, ResolverDefaults.ManifestKeys[kind], values);
        }

        manifest.Remove("preset");
        manifest.Remove("presets");
        if (wasLocalTree)
        {
            manifest.Set("materialize", "copy");
        }

        manifest.Save();
        if (wasLocalTree)
        {
            // Wave F: seed the tooling record from the files this tree already carries.
        }

        var payload = new PinPayload(
            ResolverDefaults.TemplateSource,
            entry["repo"]!.GetValue<string>(),
            tag!,
            lists.Values.Sum(values => values.Count),
            queries.Paths.ManifestPath);
        return new OperationOutcome(
            new CliResult(
                0,
                $"pinned {ResolverDefaults.TemplateSource} to {tag} ({payload.Repo}) — "
                + $"{payload.Selected} selected item(s)\n"
                + "run 'catalog.py update --apply' to replace them",
                string.Empty),
            payload);
    }

    public OperationOutcome Preset(string action, IReadOnlyList<string> names)
    {
        var adding = action == "add";
        var existing = queries.LoadManifest();
        if (existing is null && !adding)
        {
            throw new ResolverException(
                $"no manifest at {queries.Paths.ManifestPath} — nothing to remove");
        }

        var manifest = existing ?? queries.StarterManifest().Bind(queries.Paths.ManifestPath);
        var index = queries.BuildIndex(manifest);
        var engine = queries.Engine(index);
        var current = SelectionEngine.PresetNames(manifest).ToList();
        var requested = engine.ValidatePresetNames(names);
        var changed = new List<string>();
        if (adding)
        {
            foreach (var name in requested.Where(name => !current.Contains(name)))
            {
                current.Add(name);
                changed.Add(name);
            }
        }
        else
        {
            foreach (var name in requested.Where(current.Contains))
            {
                current.Remove(name);
                changed.Add(name);
            }
        }

        SetStringArray(manifest, "presets", current);
        manifest.Remove("preset");
        manifest.Save();

        var payload = new PresetMutationPayload(action, changed, current, queries.Paths.ManifestPath);
        return new OperationOutcome(
            new CliResult(
                0,
                $"{(adding ? "added" : "removed")} preset(s): {(changed.Count > 0 ? string.Join(", ", changed) : "(none)")}\n"
                + $"presets: {(current.Count > 0 ? string.Join(", ", current) : "(none)")}",
                string.Empty),
            payload);
    }

    // ── one RPC per user-level operation ────────────────────────────────────

    public OperationOutcome Install(IReadOnlyList<string> ids)
    {
        var added = Add(ids);
        if (added.Result.Code != 0)
        {
            return added;
        }

        var sync = Sync();
        return new OperationOutcome(sync.Result, sync.Report);
    }

    public OperationOutcome RemoveSelection(IReadOnlyList<string> ids, bool prune)
    {
        var removed = Remove(ids, prune: prune);
        if (removed.Result.Code != 0)
        {
            return removed;
        }

        var sync = Sync(prune: prune);
        return new OperationOutcome(sync.Result, sync.Report);
    }

    public OperationOutcome AddPresetAndSync(string name)
    {
        var preset = Preset("add", [name]);
        if (preset.Result.Code != 0)
        {
            return preset;
        }

        var sync = Sync();
        return new OperationOutcome(sync.Result, sync.Report);
    }

    public OperationOutcome PruneDeselected()
    {
        var sync = Sync(prune: true);
        return new OperationOutcome(sync.Result, sync.Report);
    }

    public OperationOutcome Upgrade(string release)
    {
        var pinned = Pin(release);
        if (pinned.Result.Code != 0)
        {
            return pinned;
        }

        return ApplyUpdates(force: false, tooling: true);
    }

    private static (List<(string Kind, string Id)> Resolved, List<string> Problems) ResolveAddTargets(
        IReadOnlyList<string> rawIds,
        string? kindFlag,
        IReadOnlyDictionary<(string Kind, string Id), CatalogItem> indexByKey)
    {
        var resolved = new List<(string Kind, string Id)>();
        var problems = new List<string>();
        foreach (var raw in rawIds)
        {
            var itemId = ItemId.Canonical(raw);
            var matches = ResolverDefaults.Kinds.Where(kind => indexByKey.ContainsKey((kind, itemId))).ToList();
            if (!string.IsNullOrEmpty(kindFlag))
            {
                if (!indexByKey.ContainsKey((kindFlag, itemId)))
                {
                    problems.Add(matches.Count > 0
                        ? $"'{ItemId.Display(itemId)}' is not a {kindFlag} (it is a {matches[0]})"
                        : UnknownMessage(raw));
                    continue;
                }

                if (!resolved.Contains((kindFlag, itemId)))
                {
                    resolved.Add((kindFlag, itemId));
                }

                continue;
            }

            if (matches.Count == 0)
            {
                problems.Add(UnknownMessage(raw));
                continue;
            }

            if (matches.Count > 1)
            {
                problems.Add(
                    $"'{ItemId.Display(itemId)}' exists as both a skill and an agent — "
                    + "pass --kind skill or --kind agent");
                continue;
            }

            if (!resolved.Contains((matches[0], itemId)))
            {
                resolved.Add((matches[0], itemId));
            }
        }

        return (resolved, problems);
    }

    private static string UnknownMessage(string raw) =>
        $"unknown id '{raw}': not in the catalog";

    private static SyncAction Action(string name, string kind, string itemId, string path, string detail = "") =>
        new(name, kind, itemId, path, detail);

    private static IEnumerable<string> Describe(IEnumerable<SyncAction> actions, IEnumerable<LockEntry> pendingPrune)
    {
        foreach (var action in actions)
        {
            var detail = string.IsNullOrEmpty(action.Detail) ? string.Empty : $" ({action.Detail})";
            yield return $"  {action.Action.ToUpperInvariant(),-8} {action.Path}{detail}";
        }

        foreach (var entry in pendingPrune)
        {
            yield return $"  {"PRUNE",-8} {entry.Path} (not selected; needs --prune)";
        }
    }

    private string Relative(string path) =>
        Path.GetRelativePath(queries.Paths.Root, path).Replace('\\', '/');

    private static string JoinLines(IEnumerable<string> lines)
    {
        var text = string.Join("\n", lines);
        return text.Length == 0 ? string.Empty : text + "\n";
    }

    private static void SetStringArray(ManifestDocument manifest, string key, IEnumerable<string> values)
    {
        manifest.Set(key, new JsonArray(values.Select(value => (JsonNode)value).ToArray()));
        _ = manifest;
    }

    private static string? ToStringOrNull(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
