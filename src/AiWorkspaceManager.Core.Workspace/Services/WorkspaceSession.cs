namespace AiWorkspaceManager.Core.Workspace;

/// <summary>The services bound to one workspace root. Caches (index/presets) live here.</summary>
public sealed class WorkspaceSession
{
    public WorkspaceSession(WorkspacePaths paths, WorkspaceQueries queries)
    {
        Paths = paths;
        Queries = queries;
        Setup = new WorkspaceSetupService(queries);
        Operations = new WorkspaceOperations(queries, Setup);
        Maintenance = new MaintenanceService(queries, Setup, Operations);
        Project = new ProjectInfoService(paths);
    }

    public WorkspacePaths Paths { get; }

    public WorkspaceQueries Queries { get; }

    public WorkspaceSetupService Setup { get; }

    public WorkspaceOperations Operations { get; }

    public MaintenanceService Maintenance { get; }

    public ProjectInfoService Project { get; }
}
