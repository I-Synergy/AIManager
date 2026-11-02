using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiWorkspaceManager.Common;
using AiWorkspaceManager.Core.Catalog;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Keeps <c>AGENTS.md</c> and the generated Copilot instructions in step with what is installed.
/// <c>AGENTS.md</c> is the source of truth: references to missing items are filtered out, the
/// installed set is listed, and the Copilot file is regenerated from it plus its Copilot-only
/// sections. This replaces the Python resolver's <c>update_instruction_docs</c>.
/// </summary>
public sealed partial class InstructionDocumentService
{
    private const string AgentsAnchor = ".ai/session-context.md";
    private const string CopilotFile = ".github/copilot-instructions.md";
    private const string MarkerPrefix = "<!-- ai-workspace-manager:";
    private const string CopilotGenerated = MarkerPrefix + "generated-from-agents -->";
    private const string CopilotReferenceStart = MarkerPrefix + "copilot-reference:start -->";
    private const string CopilotReferenceEnd = MarkerPrefix + "copilot-reference:end -->";
    private const string InstalledStart = MarkerPrefix + "installed:start -->";
    private const string InstalledEnd = MarkerPrefix + "installed:end -->";
    private const string LegacyMarkerPrefix = "<!-- ai-manager:";

    private static readonly string[] CopilotReferenceSections =
    [
        "Project Context",
        "Architecture",
        "Forbidden Technologies",
        "Patterns & Templates",
        "Naming Conventions",
        "Token Replacements",
        "Core Operational Rules",
        "Work-Type Context Mapping",
    ];

    private readonly WorkspaceQueries queries;

    public InstructionDocumentService(WorkspaceQueries queries)
    {
        this.queries = queries;
    }

    public Dictionary<string, string> Update(JsonObject? manifestRoot)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        if (manifestRoot is null)
        {
            return results;
        }

        var manifest = ManifestDocument.Temporary(manifestRoot);
        var entry = SourceCatalog.EntryNamed(manifest, ResolverDefaults.TemplateSource);
        if (!SourceCatalog.IsRemote(entry))
        {
            var sourceRoot = queries.Sources.SourceRoot(ResolverDefaults.TemplateSource, entry);
            if (WorkspacePaths.SamePath(sourceRoot, queries.Paths.Root))
            {
                return results;
            }
        }

        var catalog = ReadInstalled();
        var names = new InstalledNames(
            catalog.Skills.Select(item => item.Name).ToHashSet(StringComparer.Ordinal),
            catalog.Agents.Select(item => item.Name).ToHashSet(StringComparer.Ordinal));
        var section = InstalledSection(catalog);

        var agentsPath = Path.Combine(queries.Paths.Root, "AGENTS.md");
        var agentsRaw = File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : string.Empty;
        var agentsText = NormalizeMarkers(agentsRaw);
        if (agentsText.Length > 0)
        {
            var original = agentsRaw;
            if (agentsText.Contains(AgentsAnchor, StringComparison.Ordinal))
            {
                agentsText = FilterItemRefs(agentsText, names);
                if (InstalledBlock().IsMatch(agentsText))
                {
                    agentsText = InstalledBlock().Replace(agentsText, section).TrimEnd() + "\n";
                }
                else
                {
                    agentsText = agentsText.TrimEnd() + "\n\n" + section + "\n";
                }
            }

            if (agentsText != original)
            {
                File.WriteAllText(agentsPath, agentsText);
                results["AGENTS.md"] = "updated";
            }
            else
            {
                results["AGENTS.md"] = "unchanged";
            }
        }

        var copilotPath = Path.Combine(queries.Paths.Root, CopilotFile);
        var clients = SelectionEngine.Clients(manifest);
        if (!clients.Contains("copilot"))
        {
            if (File.Exists(copilotPath))
            {
                var existing = File.ReadAllText(copilotPath);
                if (existing.Contains(CopilotGenerated, StringComparison.Ordinal)
                    || existing.Contains(LegacyMarkerPrefix, StringComparison.Ordinal))
                {
                    File.Delete(copilotPath);
                    var parent = Path.GetDirectoryName(copilotPath);
                    if (!string.IsNullOrEmpty(parent)
                        && Directory.Exists(parent)
                        && !Directory.EnumerateFileSystemEntries(parent).Any())
                    {
                        Directory.Delete(parent);
                    }

                    results[CopilotFile] = "removed";
                }
                else
                {
                    results[CopilotFile] = "kept";
                }
            }

            return results;
        }

        if (agentsText.Length > 0)
        {
            var existing = File.Exists(copilotPath) ? File.ReadAllText(copilotPath) : null;
            var document = CopilotDocument(agentsText, existing, names);
            if (document is not null && document != existing)
            {
                var parent = Path.GetDirectoryName(copilotPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.WriteAllText(copilotPath, document);
                results[CopilotFile] = "updated";
            }
            else
            {
                results[CopilotFile] = document is not null ? "unchanged" : "kept";
            }
        }

        return results;
    }

    public IReadOnlyList<(string Name, string Description)> InstalledSkills()
    {
        var skills = new List<(string Name, string Description)>();
        var directory = Path.Combine(queries.Paths.AiDirectory, "skills");
        if (!Directory.Exists(directory))
        {
            return skills;
        }

        foreach (var skillDirectory in Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = Path.Combine(skillDirectory, "SKILL.md");
            if (!File.Exists(document))
            {
                continue;
            }

            var meta = Frontmatter.Parse(File.ReadAllText(document));
            skills.Add((
                meta.GetString("name") ?? Path.GetFileName(skillDirectory),
                FirstLine(meta.GetString("description"))));
        }

        return skills;
    }

    public IReadOnlyList<(string Name, string Description)> InstalledAgents()
    {
        var agents = new List<(string Name, string Description)>();
        var directory = Path.Combine(queries.Paths.AiDirectory, "agents");
        if (!Directory.Exists(directory))
        {
            return agents;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.md").OrderBy(path => path, StringComparer.Ordinal))
        {
            var meta = Frontmatter.Parse(File.ReadAllText(file));
            agents.Add((
                meta.GetString("name") ?? Path.GetFileNameWithoutExtension(file),
                FirstLine(meta.GetString("description"))));
        }

        return agents;
    }

    private InstalledCatalog ReadInstalled() =>
        new(InstalledSkills(), InstalledAgents());

    private static string InstalledSection(InstalledCatalog catalog)
    {
        var lines = new List<string> { "## Installed skills and agents", string.Empty, InstalledStart };
        foreach (var (kind, title) in new[] { ("skills", "Skills"), ("agents", "Agents") })
        {
            var items = kind == "skills" ? catalog.Skills : catalog.Agents;
            if (items.Count == 0)
            {
                continue;
            }

            lines.Add($"### {title}");
            foreach (var (name, description) in items)
            {
                lines.Add(string.IsNullOrEmpty(description)
                    ? $"- `{name}`"
                    : $"- `{name}` — {description}");
            }

            lines.Add(string.Empty);
        }

        lines.Add(InstalledEnd);
        return string.Join("\n", lines);
    }

    private static string FilterItemRefs(string text, InstalledNames names)
    {
        var kept = new List<string>();
        foreach (var line in text.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            var missing = false;
            var updated = ItemRef().Replace(line, match =>
            {
                var skill = match.Groups["skill"].Value;
                if (skill.Length > 0)
                {
                    if (names.Skills.Contains(skill))
                    {
                        return match.Value;
                    }
                }
                else if (names.Agents.Contains(match.Groups["agent"].Value))
                {
                    return match.Value;
                }

                missing = true;
                return string.Empty;
            });

            if (!missing)
            {
                kept.Add(line);
                continue;
            }

            updated = TidyReferenceLine(updated);
            if (updated.TrimStart().StartsWith('|')
                && !Regex.IsMatch(updated, "`\\.ai/", RegexOptions.CultureInvariant))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(updated))
            {
                continue;
            }

            kept.Add(updated);
        }

        var result = string.Join("\n", kept);
        return text.EndsWith('\n') ? result + "\n" : result;
    }

    private static string TidyReferenceLine(string line)
    {
        while (Regex.IsMatch(line, @",\s*,", RegexOptions.CultureInvariant))
        {
            line = Regex.Replace(line, @",\s*,", ", ", RegexOptions.CultureInvariant);
        }

        line = Regex.Replace(line, @"\|\s*,", "|", RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @",\s*\|", " |", RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @";\s*invocable as\s*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @"\s+as\s*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @"\s+and\s*\.?\s*$", string.Empty, RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @"\s+or\s*\.?\s*$", string.Empty, RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @";\s*$", string.Empty, RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @"[ \t]{2,}", " ", RegexOptions.CultureInvariant);
        return line.TrimEnd();
    }

    private static string CopilotReference(string text)
    {
        if (text.Contains(CopilotReferenceStart, StringComparison.Ordinal)
            && text.Contains(CopilotReferenceEnd, StringComparison.Ordinal))
        {
            var start = text.IndexOf(CopilotReferenceStart, StringComparison.Ordinal) + CopilotReferenceStart.Length;
            var end = text.IndexOf(CopilotReferenceEnd, StringComparison.Ordinal);
            return text[start..end].Trim();
        }

        var parts = CopilotReferenceSections
            .Select(title => Section(text, title))
            .Where(part => part.Length > 0);
        return string.Join("\n\n", parts);
    }

    private static string? CopilotDocument(string agentsText, string? existing, InstalledNames names)
    {
        var reference = string.Empty;
        if (existing is not null)
        {
            existing = NormalizeMarkers(existing);
            var generated = existing.Contains(CopilotGenerated, StringComparison.Ordinal);
            var template = existing.Contains("## Work-Type Context Mapping", StringComparison.Ordinal)
                || existing.Contains("## Project Context", StringComparison.Ordinal);
            if (!generated && !template)
            {
                return null;
            }

            reference = FilterItemRefs(CopilotReference(existing), names).Trim();
        }

        var parts = new List<string>
        {
            "# GitHub Copilot Instructions",
            string.Empty,
            CopilotGenerated,
            "<!-- Generated from AGENTS.md. Edit AGENTS.md, not this file. -->",
            string.Empty,
            CopilotPaths(agentsText.Trim()),
        };
        if (reference.Length > 0)
        {
            parts.Add(string.Empty);
            parts.Add(CopilotReferenceStart);
            parts.Add(CopilotPaths(reference));
            parts.Add(CopilotReferenceEnd);
        }

        return string.Join("\n", parts).TrimEnd() + "\n";
    }

    private static string NormalizeMarkers(string text) =>
        (text ?? string.Empty).Replace(LegacyMarkerPrefix, MarkerPrefix, StringComparison.Ordinal);

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
    }

    private static string Section(string text, string title)
    {
        var match = SectionPattern(title).Match(text);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static string CopilotPaths(string text) => ClaudePathPattern().Replace(text, ".github/");

    [GeneratedRegex(
        "\\.ai/(?:skills/(?<skill>[A-Za-z0-9._-]+)/SKILL\\.md|agents/(?<agent>[A-Za-z0-9._-]+)\\.md)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ItemRef();

    [GeneratedRegex(
        "## Installed skills and agents\\s*\\n.*?<!-- ai-workspace-manager:installed:end -->",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex InstalledBlock();

    [GeneratedRegex("\\.claude/(?!settings\\.json|settings\\.local\\.json)", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudePathPattern();

    [GeneratedRegex("^## {TITLE}\\s*$.*?(?=^## |\\Z)", RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SectionTemplate();

    private static Regex SectionPattern(string title) =>
        new("^## " + Regex.Escape(title) + "\\s*$.*?(?=^## |\\Z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private sealed record InstalledCatalog(
        IReadOnlyList<(string Name, string Description)> Skills,
        IReadOnlyList<(string Name, string Description)> Agents);

    private sealed record InstalledNames(HashSet<string> Skills, HashSet<string> Agents);
}
