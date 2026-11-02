using System.Globalization;
using System.Text.Json.Nodes;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// The project name/intention form: stores <c>.ai/project/project.json</c> and fills the template
/// documents only while they still carry the template markers. A customized document is never
/// rewritten. Replaces the Python resolver's <c>cmd_project</c>.
/// </summary>
public sealed class ProjectInfoService
{
    private const string ReadmeMarker = "[Describe the project's purpose.]";

    private readonly WorkspacePaths paths;

    public ProjectInfoService(WorkspacePaths paths)
    {
        this.paths = paths;
    }

    public string DetectedName()
    {
        foreach (var pattern in new[] { "*.slnx", "*.sln" })
        {
            var solutions = Directory.EnumerateFiles(paths.Root, pattern)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (solutions.Count > 0)
            {
                return Path.GetFileNameWithoutExtension(solutions[0]);
            }
        }

        return Path.GetFileName(paths.Root.TrimEnd(Path.DirectorySeparatorChar));
    }

    public JsonObject Load()
    {
        var data = JsonFiles.ReadObject(paths.ProjectInfoPath);
        return data ?? new JsonObject();
    }

    public ProjectInfoPayload Read()
    {
        var current = Load();
        var detected = DetectedName();
        return new ProjectInfoPayload(
            current["name"]?.GetValue<string>() ?? detected,
            current["intention"]?.GetValue<string>() ?? string.Empty,
            detected,
            [],
            []);
    }

    /// <summary>Store the name/intention and fill the still-template documents.</summary>
    public ProjectInfoPayload Describe(string name, string intention)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Common.ResolverException("a project name is required");
        }

        return _Describe(name.Trim(), intention.Trim());
    }

    /// <summary>Create a starter README when the project has none.</summary>
    public string EnsureReadme()
    {
        Directory.CreateDirectory(paths.Root);
        var path = Path.Combine(paths.Root, "README.md");
        if (File.Exists(path))
        {
            return "kept";
        }

        File.WriteAllText(path, $"# {DetectedName()}\n\n{ReadmeMarker}\n");
        return "created";
    }

    private ProjectInfoPayload _Describe(string name, string intention)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var purpose = intention.Length > 0 ? intention : "[What this application does]";
        var written = new List<string>();
        var skipped = new List<string>();

        var session = Path.Combine(paths.AiDirectory, "session-context.md");
        if (FillTemplate(
            session,
            "{ApplicationName}",
            [
                ("# Session Context Template (CUSTOMIZE THIS)\n\n"
                 + "**Instructions:** Copy this to `.ai/session-context.md` and customize for your project.\n\n", ""),
                ("# {ApplicationName} Session Context", $"# {name} Session Context"),
                ("[{ApplicationName}]", name),
                ("[What this application does]", purpose),
                ("[DateTime]", today),
                ("**Updated By:** [Claude Code | Copilot | Reasonix Code | User]",
                 "**Updated By:** AI Workspace Manager"),
            ]))
        {
            written.Add(".ai/session-context.md");
        }
        else if (File.Exists(session))
        {
            var text = File.ReadAllText(session);
            if (text.Contains("**Updated By:** AI Manager", StringComparison.Ordinal)
                || text.Contains("**Updated By:** AI Workspace Manager", StringComparison.Ordinal))
            {
                if (intention.Length > 0)
                {
                    text = UpdateLine(text, "**Purpose:**", intention);
                }

                text = UpdateLine(text, "**Last Updated:**", today);
                File.WriteAllText(session, text);
                written.Add(".ai/session-context.md");
            }
            else
            {
                skipped.Add(".ai/session-context.md");
            }
        }

        foreach (var (relative, title) in new[]
        {
            (".ai/project/architecture.md", "Project Architecture"),
            (".ai/project/domains.md", "Business Domains"),
        })
        {
            var path = Path.Combine(paths.Root, relative);
            if (FillTemplate(
                path,
                "(CUSTOMIZE THIS)",
                [($"# {title} (CUSTOMIZE THIS)", $"# {name} — {title}")],
                [$"**Project:** {name}", $"**Intention:** {purpose}", string.Empty]))
            {
                written.Add(relative);
            }
            else if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                if (text.Contains("**Project:**", StringComparison.Ordinal)
                    && text.Contains("**Intention:**", StringComparison.Ordinal))
                {
                    text = UpdateLine(text, "**Project:**", name);
                    if (intention.Length > 0)
                    {
                        text = UpdateLine(text, "**Intention:**", intention);
                    }

                    File.WriteAllText(path, text);
                    written.Add(relative);
                }
                else
                {
                    skipped.Add(relative);
                }
            }
        }

        var info = new JsonObject
        {
            ["version"] = 1,
            ["name"] = name,
            ["intention"] = intention,
            ["updated"] = today,
        };
        JsonFiles.Write(paths.ProjectInfoPath, info);

        foreach (var relative in FillApplicationName(name))
        {
            if (!written.Contains(relative))
            {
                written.Add(relative);
            }
        }

        var readme = Path.Combine(paths.Root, "README.md");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme, $"# {name}\n\n{(intention.Length > 0 ? intention : ReadmeMarker)}\n");
            written.Add("README.md");
        }
        else
        {
            var text = File.ReadAllText(readme);
            if (text.Contains(ReadmeMarker, StringComparison.Ordinal))
            {
                text = text.Replace(ReadmeMarker, intention.Length > 0 ? intention : ReadmeMarker, StringComparison.Ordinal);
                var lines = text.Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (lines[index].StartsWith("# ", StringComparison.Ordinal))
                    {
                        lines[index] = $"# {name}";
                        break;
                    }
                }

                File.WriteAllText(readme, string.Join("\n", lines));
                written.Add("README.md");
            }
        }

        return new ProjectInfoPayload(name, intention, DetectedName(), written, skipped);
    }

    private List<string> FillApplicationName(string name)
    {
        var written = new List<string>();
        foreach (var path in ScaffoldFiles())
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".md" or ".txt" or ".feature" or ".json"))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (!text.Contains("{ApplicationName}", StringComparison.Ordinal))
            {
                continue;
            }

            File.WriteAllText(path, text.Replace("{ApplicationName}", name, StringComparison.Ordinal));
            written.Add(Path.GetRelativePath(paths.Root, path).Replace('\\', '/'));
        }

        return written;
    }

    private IEnumerable<string> ScaffoldFiles()
    {
        yield return Path.Combine(paths.AiDirectory, "session-context.md");
        yield return Path.Combine(paths.Root, "AGENTS.md");
        yield return Path.Combine(paths.Root, ".github", "copilot-instructions.md");
        foreach (var name in WorkspaceSetupService.TemplateDirectories)
        {
            var directory = Path.Combine(paths.AiDirectory, name);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static bool FillTemplate(
        string path,
        string marker,
        IReadOnlyList<(string Old, string New)> replacements,
        IReadOnlyList<string>? preamble = null)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path);
        if (!text.Contains(marker, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var (oldValue, newValue) in replacements)
        {
            text = text.Replace(oldValue, newValue, StringComparison.Ordinal);
        }

        if (preamble is not null)
        {
            text = InsertAfterTitle(text, preamble);
        }

        File.WriteAllText(path, text);
        return true;
    }

    private static string InsertAfterTitle(string text, IReadOnlyList<string> lines)
    {
        var parts = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].StartsWith("# ", StringComparison.Ordinal))
            {
                var combined = parts.Take(index + 1)
                    .Concat([string.Empty])
                    .Concat(lines)
                    .Concat(parts.Skip(index + 1));
                return string.Join("\n", combined) + "\n";
            }
        }

        return string.Join("\n", lines.Concat(parts)) + "\n";
    }

    private static string UpdateLine(string text, string label, string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            "^" + System.Text.RegularExpressions.Regex.Escape(label) + ".*$",
            label + " " + value,
            System.Text.RegularExpressions.RegexOptions.Multiline,
            TimeSpan.FromSeconds(1));
}
