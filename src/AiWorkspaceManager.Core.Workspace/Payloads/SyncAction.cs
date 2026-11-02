namespace AiWorkspaceManager.Core.Workspace;

/// <summary>One planned or performed sync step.</summary>
public sealed record SyncAction(
    string Action,
    string Kind,
    string Id,
    string Path,
    string Detail);
