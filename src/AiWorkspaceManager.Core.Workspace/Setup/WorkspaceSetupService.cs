using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Workspace setup: vocabulary and tooling ownership, the supporting <c>.ai/</c> folders, agent
/// configuration, gitignore entries and the one-time catalog-layout migration.
/// </summary>
public sealed class WorkspaceSetupService
{
    public static readonly IReadOnlyList<string> TemplateDirectories =
        ["analysis", "checklists", "completed", "patterns", "plans", "progress", "project", "reference"];

    public static readonly IReadOnlyList<string> TemplateFiles = [".ai/session-context.md"];

    public static readonly IReadOnlyList<string> AgentFiles =
        ["AGENTS.md", ".github/copilot-instructions.md", ".claude/settings.json", ".gitattributes"];

    public static readonly IReadOnlyList<string> GitignoreEntries =
    [
        "__pycache__/",
        "*.py[cod]",
        ".pytest_cache/",
        ".ai/tmp/",
        ".claude/skills/",
        ".claude/agents/",
        ".github/skills/",
        ".github/agents/",
        ".github/instructions/",
        ".github/prompts/",
        ".claude/commands/",
        ".claude/instructions/",
        ".reasonix/skills/",
        ".reasonix/agents/",
    ];

    /// <summary>
    /// Template-owned paths. The resolver itself moved into this extension and is no longer a
    /// script, but the list is kept so an existing project's copy is still recognised as
    /// template-owned rather than adopted as project-created.
    /// </summary>
    public static readonly IReadOnlyList<string> ToolingFiles =
    [
        ".ai/scripts/catalog.py",
        ".ai/scripts/sync-platforms.py",
        ".ai/scripts/sync-skills.py",
        ".ai/scripts/sync-agents.py",
        ".ai/scripts/upgrade-template.py",
        ".ai/scripts/hygiene-lint.py",
        ".ai/scripts/migrate-to-ai.py",
        ".ai/tests/validate-release.py",
        "catalog/presets.json",
        "catalog/categories.json",
        "catalog/schemas/ai.schema.json",
        "catalog/schemas/index.schema.json",
        "catalog/schemas/lock.schema.json",
    ];

    private static readonly HashSet<string> BundledPreferred = new(StringComparer.Ordinal)
    {
        ".ai/scripts/sync-platforms.py",
        ".ai/tests/validate-release.py",
    };

    private readonly WorkspaceQueries queries;

    public WorkspaceSetupService(WorkspaceQueries queries)
    {
        this.queries = queries;
    }

    private WorkspacePaths Paths => queries.Paths;

    /// <summary>Move a project's root <c>catalog/</c> into <c>.ai/catalog/</c> (one-time fix).</summary>
    public bool MigrateCatalogDirectory()
    {
        var manifest = queries.LoadManifest();
        if (manifest is null || !SourceCatalog.IsRemote(SourceCatalog.EntryNamed(manifest, ResolverDefaults.TemplateSource)))
        {
            return false;
        }

        var rootCatalog = Path.Combine(Paths.Root, "catalog");
        var aiCatalog = Path.Combine(Paths.AiDirectory, "catalog");
        if (!Directory.Exists(rootCatalog) || Directory.Exists(aiCatalog))
        {
            return false;
        }

        Directory.CreateDirectory(Paths.AiDirectory);
        Directory.Move(rootCatalog, aiCatalog);
        var schema = manifest.GetString("$schema") ?? string.Empty;
        if (schema.StartsWith("../catalog/", StringComparison.Ordinal))
        {
            manifest.Set("$schema", "catalog/" + schema["../catalog/".Length..]);
            manifest.Save();
        }

        if (File.Exists(Paths.ToolingRecordPath))
        {
            var record = LoadToolingRecord();
            if (record["files"] is JsonArray files)
            {
                foreach (var file in files.OfType<JsonObject>())
                {
                    var path = file["path"]?.GetValue<string>() ?? string.Empty;
                    if (path.StartsWith("catalog/", StringComparison.Ordinal))
                    {
                        file["path"] = ".ai/" + path;
                    }
                }

                record["files"] = new JsonArray(files
                    .OfType<JsonObject>()
                    .OrderBy(file => file["path"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal)
                    .Select(file => (JsonNode)file.DeepClone())
                    .ToArray());
            }

            SaveToolingRecord(record);
        }

        return true;
    }

    /// <summary>Copy the template's supporting <c>.ai/</c> folders when a project lacks them.</summary>
    public FileCopySummary InstallScaffold(string sourceRoot)
    {
        var copied = new List<string>();
        var skipped = new List<string>();
        if (WorkspacePaths.SamePath(sourceRoot, Paths.Root))
        {
            return new FileCopySummary(copied, skipped);
        }

        foreach (var source in TemplateScaffoldFiles(sourceRoot))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var key = relative.Replace('\\', '/');
            var target = Path.Combine(Paths.Root, relative);
            if (File.Exists(target))
            {
                skipped.Add(key);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, target);
            copied.Add(key);
        }

        return new FileCopySummary(copied, skipped);
    }

    /// <summary>Write the missing client-facing files for the selected clients.</summary>
    public FileCopySummary InstallAgentConfig(JsonObject? manifest)
    {
        var copied = new List<string>();
        var skipped = new List<string>();
        var sourceRoot = queries.Sources.TemplateRoot(manifest is null ? queries.ManifestOrStarter() : ManifestDocument.Temporary(manifest));
        if (WorkspacePaths.SamePath(sourceRoot, Paths.Root))
        {
            return new FileCopySummary(copied, skipped);
        }

        var clients = SelectionEngine.Clients(
            manifest is null ? queries.ManifestOrStarter() : ManifestDocument.Temporary(manifest));
        foreach (var relative in AgentFiles)
        {
            if (relative == ".claude/settings.json" && !clients.Contains("claude"))
            {
                continue;
            }

            if (relative == ".github/copilot-instructions.md" && !clients.Contains("copilot"))
            {
                continue;
            }

            var source = Path.Combine(sourceRoot, relative);
            if (!File.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(Paths.Root, relative);
            if (File.Exists(target))
            {
                skipped.Add(relative);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, target);
            copied.Add(relative);
        }

        return new FileCopySummary(copied, skipped);
    }

    /// <summary>Record pre-existing template files as template-owned.</summary>
    public void SeedToolingRecord()
    {
        var record = LoadToolingRecord();
        var recorded = RecordedTooling(record);
        var added = false;
        foreach (var relative in ToolingFiles)
        {
            var destination = ToolingDestination(relative);
            var target = Path.Combine(Paths.Root, destination);
            if (File.Exists(target) && !recorded.ContainsKey(destination))
            {
                recorded[destination] = new JsonObject
                {
                    ["path"] = destination,
                    ["sha256"] = ContentHash.FileSha(target),
                    ["origin"] = "template",
                };
                added = true;
            }
        }

        if (!added)
        {
            return;
        }

        record["version"] = 1;
        record["source"] = ResolverDefaults.TemplateSource;
        record["files"] = SortedFiles(recorded.Values);
        SaveToolingRecord(record);
    }

    /// <summary>Bring the template's vocabulary (and any scripts it still ships) into the project.</summary>
    public ToolingSummary InstallTooling(string sourceRoot, bool overwrite = false, string? reference = null)
    {
        var copied = new List<string>();
        var skipped = new List<string>();
        var unchanged = new List<string>();
        var missing = new List<string>();
        var protectedFiles = new List<string>();
        if (WorkspacePaths.SamePath(sourceRoot, Paths.Root))
        {
            return new ToolingSummary(copied, skipped, unchanged, missing, protectedFiles);
        }

        var bundled = queries.BundledToolingRoot;
        var record = LoadToolingRecord();
        var recorded = RecordedTooling(record);
        var changed = false;

        foreach (var relative in TemplateToolingFiles(sourceRoot))
        {
            var destination = ToolingDestination(relative);
            var target = Path.Combine(Paths.Root, destination);
            var source = Path.Combine(sourceRoot, relative);
            var origin = "template";
            if (BundledPreferred.Contains(relative) && !string.IsNullOrEmpty(bundled))
            {
                var fallback = Path.Combine(bundled, relative);
                if (File.Exists(fallback))
                {
                    source = fallback;
                    origin = "bundled";
                }
            }

            if (!File.Exists(source))
            {
                var fallback = string.IsNullOrEmpty(bundled) ? null : Path.Combine(bundled, relative);
                if (fallback is not null && File.Exists(fallback))
                {
                    source = fallback;
                    origin = "bundled";
                }
                else
                {
                    missing.Add(destination);
                    continue;
                }
            }

            if (File.Exists(target))
            {
                var current = ContentHash.FileSha(target);
                if (!recorded.TryGetValue(destination, out var entry))
                {
                    recorded[destination] = new JsonObject
                    {
                        ["path"] = destination,
                        ["sha256"] = current,
                        ["origin"] = origin,
                    };
                    changed = true;
                }
                else if (!string.Equals(current, entry["sha256"]?.GetValue<string>(), StringComparison.Ordinal))
                {
                    protectedFiles.Add(destination);
                    continue;
                }

                if (!overwrite)
                {
                    skipped.Add(destination);
                    continue;
                }

                if (string.Equals(ContentHash.FileSha(source), current, StringComparison.Ordinal))
                {
                    unchanged.Add(destination);
                    continue;
                }
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, target, overwrite: true);
            recorded[destination] = new JsonObject
            {
                ["path"] = destination,
                ["sha256"] = ContentHash.FileSha(target),
                ["origin"] = origin,
            };
            copied.Add(destination);
            changed = true;
        }

        if (changed || !File.Exists(Paths.ToolingRecordPath))
        {
            record["version"] = 1;
            record["source"] = ResolverDefaults.TemplateSource;
            if (!string.IsNullOrEmpty(reference))
            {
                record["ref"] = reference;
            }

            record["files"] = SortedFiles(recorded.Values);
            SaveToolingRecord(record);
        }

        return new ToolingSummary(copied, skipped, unchanged, missing, protectedFiles);
    }

    /// <summary>Ignore resolver state and the platform junctions; existing lines are kept.</summary>
    public string EnsureGitignore()
    {
        var path = Path.Combine(Paths.Root, ".gitignore");
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var present = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.TrimEnd('/'))
            .ToHashSet(StringComparer.Ordinal);
        var missing = GitignoreEntries.Where(entry => !present.Contains(entry.TrimEnd('/'))).ToList();
        if (missing.Count == 0)
        {
            return "kept";
        }

        var block = "# AI Workspace Manager: resolver cache and platform junctions\n"
            + string.Join("\n", missing) + "\n";
        if (text.Length == 0)
        {
            File.WriteAllText(path, block);
            return "created";
        }

        if (!text.EndsWith('\n'))
        {
            text += "\n";
        }

        if (!text.EndsWith("\n\n", StringComparison.Ordinal))
        {
            text += "\n";
        }

        File.WriteAllText(path, text + block);
        return "updated";
    }

    public static string ToolingDestination(string relative) =>
        relative.StartsWith("catalog/", StringComparison.Ordinal) ? ".ai/" + relative : relative;

    public static List<string> TemplateToolingFiles(string sourceRoot)
    {
        var files = new List<string>(ToolingFiles);
        var scripts = Path.Combine(sourceRoot, ".ai", "scripts");
        if (Directory.Exists(scripts))
        {
            foreach (var entry in Directory.EnumerateFiles(scripts, "*.py").OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(sourceRoot, entry).Replace('\\', '/');
                if (!files.Contains(relative))
                {
                    files.Add(relative);
                }
            }
        }

        var tests = Path.Combine(sourceRoot, ".ai", "tests");
        if (Directory.Exists(tests))
        {
            foreach (var entry in Directory.EnumerateFiles(tests, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (entry.Contains("__pycache__", StringComparison.Ordinal)
                    || entry.EndsWith(".pyc", StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(sourceRoot, entry).Replace('\\', '/');
                if (!files.Contains(relative))
                {
                    files.Add(relative);
                }
            }
        }

        return files;
    }

    public JsonObject LoadToolingRecord()
    {
        var data = JsonFiles.ReadObject(Paths.ToolingRecordPath);
        return data ?? new JsonObject
        {
            ["version"] = 1,
            ["source"] = ResolverDefaults.TemplateSource,
            ["files"] = new JsonArray(),
        };
    }

    public void SaveToolingRecord(JsonObject record) => JsonFiles.Write(Paths.ToolingRecordPath, record);

    private static List<string> TemplateScaffoldFiles(string sourceRoot)
    {
        var files = new List<string>();
        foreach (var name in TemplateDirectories)
        {
            var directory = Path.Combine(sourceRoot, ".ai", name);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains("__pycache__", StringComparison.Ordinal)
                    && !file.EndsWith(".pyc", StringComparison.Ordinal))
                .OrderBy(file => file, StringComparer.Ordinal));
        }

        foreach (var relative in TemplateFiles)
        {
            var source = Path.Combine(sourceRoot, relative);
            if (File.Exists(source))
            {
                files.Add(source);
            }
        }

        return files;
    }

    private static Dictionary<string, JsonObject> RecordedTooling(JsonObject record)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (record["files"] is not JsonArray files)
        {
            return result;
        }

        foreach (var entry in files.OfType<JsonObject>())
        {
            var path = entry["path"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(path))
            {
                result[path] = entry;
            }
        }

        return result;
    }

    private static JsonArray SortedFiles(IEnumerable<JsonObject> entries) =>
        new(entries
            .OrderBy(entry => entry["path"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal)
            .Select(entry => (JsonNode)entry.DeepClone())
            .ToArray());
}
