namespace AiWorkspaceManager.Core.Workspace;

/// <summary>
/// Composition root for the sidecar. One instance per process; the host sends the workspace root
/// with each request because the extension can switch folders without restarting the sidecar.
/// </summary>
public sealed class WorkspaceServices
{
    private readonly bool offline;
    private readonly string? toolingRoot;
    private readonly Dictionary<string, WorkspaceSession> sessions = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceServices()
        : this(Environment.GetEnvironmentVariable("CATALOG_TOOLING"), IsOffline())
    {
    }

    public WorkspaceServices(string? toolingRoot, bool offline)
    {
        this.toolingRoot = string.IsNullOrWhiteSpace(toolingRoot) ? null : toolingRoot;
        this.offline = offline;
    }

    public bool Offline => offline;

    public string? ToolingRoot => toolingRoot;

    public WorkspaceSession Session(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A workspace root is required.", nameof(root));
        }

        var normalized = Path.GetFullPath(root);
        if (sessions.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        var paths = new WorkspacePaths(normalized);
        var queries = new WorkspaceQueries(
            paths,
            new GitSourceFetcher(paths, offline),
            offline,
            toolingRoot);
        var session = new WorkspaceSession(paths, queries);
        sessions[normalized] = session;
        return session;
    }

    private static bool IsOffline()
    {
        var value = (Environment.GetEnvironmentVariable("CATALOG_OFFLINE") ?? string.Empty).Trim();
        return value is not ("" or "0" or "false");
    }
}
