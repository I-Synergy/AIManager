using System.Text.Json.Nodes;
using AiWorkspaceManager.Common;

namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Folder-level junctions from <c>.ai/skills|agents|instructions|prompts</c> into each selected
/// client's folders. Disabling a client removes only its junctions; a real directory is never
/// deleted. This is the C# replacement for <c>sync-platforms.py</c>.
/// </summary>
public sealed class PlatformMirrorService
{
    private static readonly (string Client, string Source, string Target)[] Junctions =
    [
        ("claude", ".ai/skills", ".claude/skills"),
        ("copilot", ".ai/skills", ".github/skills"),
        ("reasonix", ".ai/skills", ".reasonix/skills"),
        ("claude", ".ai/agents", ".claude/agents"),
        ("copilot", ".ai/agents", ".github/agents"),
        ("reasonix", ".ai/agents", ".reasonix/agents"),
        ("claude", ".ai/instructions", ".claude/instructions"),
        ("copilot", ".ai/instructions", ".github/instructions"),
        ("claude", ".ai/prompts", ".claude/commands"),
        ("copilot", ".ai/prompts", ".github/prompts"),
    ];

    private static readonly string[] ClientDirectories = [".claude", ".github", ".reasonix"];

    private readonly WorkspacePaths paths;

    public PlatformMirrorService(WorkspacePaths paths)
    {
        this.paths = paths;
    }

    public MirrorReport Sync(ManifestDocument manifest, bool dryRun = false)
    {
        var clients = SelectionEngine.Clients(manifest);
        var prefix = dryRun ? "[DRY RUN] " : string.Empty;
        var lines = new List<string>
        {
            $"{prefix}Ensuring folder-level junctions for: "
            + (string.Join(", ", ResolverDefaults.Clients.Where(clients.Contains)) is { Length: > 0 } names
                ? names
                : "(no clients)"),
        };

        var changed = 0;
        foreach (var (client, sourceRelative, targetRelative) in Junctions)
        {
            if (clients.Contains(client))
            {
                var result = SyncJunction(sourceRelative, targetRelative, dryRun);
                if (result == "OK")
                {
                    lines.Add($"  OK   {targetRelative} -> {sourceRelative}");
                }
                else if (result == "SKIP")
                {
                    lines.Add($"  SKIP {targetRelative} -> {sourceRelative} (source missing)");
                }
                else
                {
                    var label = dryRun ? $"WOULD {result}" : result;
                    lines.Add($"  {label} {targetRelative} -> {sourceRelative}");
                    changed++;
                }
            }
            else
            {
                var result = RemoveJunction(targetRelative, dryRun);
                if (result == "REMOVED")
                {
                    lines.Add($"  {(dryRun ? "WOULD REMOVE" : "REMOVED")} {targetRelative} ({client} disabled)");
                    changed++;
                }
                else if (result == "KEEP")
                {
                    lines.Add($"  KEEP {targetRelative} (not a junction; {client} disabled)");
                }
            }
        }

        var removed = PruneEmptyClientDirectories(dryRun);
        foreach (var relative in removed)
        {
            lines.Add($"  {(dryRun ? "WOULD REMOVE" : "REMOVED")} {relative} (empty)");
        }

        lines.Add($"\n{changed} item(s) {(dryRun ? "would be " : string.Empty)}changed.");
        return new MirrorReport(changed, lines, removed);
    }

    private string SyncJunction(string sourceRelative, string targetRelative, bool dryRun)
    {
        var target = Path.Combine(paths.Root, targetRelative);
        var source = Path.GetFullPath(Path.Combine(paths.Root, sourceRelative));
        if (!Directory.Exists(source))
        {
            return "SKIP";
        }

        string action;
        if (PathOps.ExistsOrLink(target))
        {
            action = PathOps.IsLink(target) && FirstEntryReachable(target, source) ? "OK" : "RECREATE";
        }
        else
        {
            action = "CREATE";
        }

        if (action == "OK" || dryRun)
        {
            return action;
        }

        if (PathOps.ExistsOrLink(target))
        {
            Materializer.RemovePath(target);
        }

        return Materializer.CreateLink(target, source)
            ? action
            : "FAIL — could not create junction/symlink";
    }

    private string RemoveJunction(string targetRelative, bool dryRun)
    {
        var target = Path.Combine(paths.Root, targetRelative);
        if (!PathOps.ExistsOrLink(target))
        {
            return "ABSENT";
        }

        if (!PathOps.IsLink(target))
        {
            return "KEEP";
        }

        if (!dryRun)
        {
            Materializer.RemovePath(target);
        }

        return "REMOVED";
    }

    private List<string> PruneEmptyClientDirectories(bool dryRun)
    {
        var removed = new List<string>();
        foreach (var relative in ClientDirectories)
        {
            var path = Path.Combine(paths.Root, relative);
            if (!Directory.Exists(path) || PathOps.IsLink(path))
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                continue;
            }

            if (!dryRun)
            {
                try
                {
                    Directory.Delete(path);
                }
                catch (IOException)
                {
                    continue;
                }
            }

            removed.Add(relative);
        }

        return removed;
    }

    private static bool FirstEntryReachable(string target, string source)
    {
        var first = Directory.EnumerateFileSystemEntries(source).FirstOrDefault();
        return first is not null
            && PathOps.ExistsOrLink(Path.Combine(target, Path.GetFileName(first)));
    }
}
