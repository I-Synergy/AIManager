namespace AiWorkspaceManager.Core.Workspace;

public sealed record SyncPerformed(
    string Action,
    string Kind,
    string Id,
    string Path,
    bool Ok,
    bool Deleted = false,
    int Markers = 0);
